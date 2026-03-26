packer {
  required_plugins {
    azure = {
      version = ">= 2.0.0"
      source  = "github.com/hashicorp/azure"
    }
  }
}

variable "application_name" {
  type    = string
  default = "Bitwarden"
}

variable "application_version" {
  type    = string
  default = "${env("AZURE_IMG_VERSION")}"
}

variable "apt_packages" {
  type    = string
  default = "fail2ban ca-certificates curl gnupg"
}

variable "docker_packages" {
  type    = string
  default = "docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin"
}

variable "subscription_id" {
  type    = string
  default = "${env("AZURE_SUBSCRIPTION_ID")}"
}

variable "resource_group" {
  type    = string
  default = "${env("AZURE_RESOURCE_GROUP")}"
}

variable "gallery_name" {
  type    = string
  default = "${env("AZURE_GALLERY_NAME")}"
}

variable "gallery_image_name" {
  type    = string
  default = "${env("AZURE_GALLERY_IMAGE_NAME")}"
}

variable "location" {
  type    = string
  default = "East US"
}

variable "github_run_id" {
  type    = string
  default = "${env("GITHUB_RUN_ID")}"
}

# "timestamp" template function replacement
locals { timestamp = regex_replace(timestamp(), "[- TZ:]", "") }

locals {
  image_name = "bitwarden-22-04-${local.timestamp}"
}

source "azure-arm" "bitwarden_self_host" {
  use_azure_cli_auth = true
  subscription_id    = var.subscription_id

  os_type         = "Linux"
  image_publisher = "Canonical"
  image_offer     = "0001-com-ubuntu-server-jammy"
  image_sku       = "22_04-lts-gen2"

  build_resource_group_name = var.resource_group
  vm_size                   = "Standard_B2s"

  managed_image_name                = local.image_name
  managed_image_resource_group_name = var.resource_group

  shared_image_gallery_destination {
    subscription        = var.subscription_id
    resource_group      = var.resource_group
    gallery_name        = var.gallery_name
    image_name          = var.gallery_image_name
    image_version       = var.application_version
    replication_regions = [var.location]
  }

  azure_tags = {
    application = "bitwarden-packer-build"
    github_run  = "github-run-${var.github_run_id}"
  }
}

build {
  sources = ["source.azure-arm.bitwarden_self_host"]

  provisioner "shell" {
    inline = ["cloud-init status --wait"]
  }

  # Upload individual files to /tmp staging area (azure-arm connects as a non-root user)
  provisioner "file" {
    source      = "files/etc/update-motd.d/99-bitwarden-welcome"
    destination = "/tmp/99-bitwarden-welcome"
  }

  provisioner "file" {
    source      = "files/etc/ufw/applications.d/bitwarden"
    destination = "/tmp/bitwarden-ufw"
  }

  provisioner "file" {
    source      = "files/opt/bitwarden/install-bitwarden.sh"
    destination = "/tmp/install-bitwarden.sh"
  }

  provisioner "file" {
    source      = "files/var/lib/cloud/scripts/per-instance/001_onboot"
    destination = "/tmp/001_onboot"
  }

  provisioner "file" {
    source      = "files/etc/profile.d/bitwarden-install.sh"
    destination = "/tmp/bitwarden-install.sh"
  }

  # Move staged files to their final system locations
  provisioner "shell" {
    inline = [
      "sudo mkdir -p /etc/update-motd.d /etc/ufw/applications.d /opt/bitwarden /var/lib/cloud/scripts/per-instance",
      "sudo mv /tmp/99-bitwarden-welcome /etc/update-motd.d/99-bitwarden-welcome",
      "sudo mv /tmp/bitwarden-ufw /etc/ufw/applications.d/bitwarden",
      "sudo mv /tmp/install-bitwarden.sh /opt/bitwarden/install-bitwarden.sh",
      "sudo mv /tmp/001_onboot /var/lib/cloud/scripts/per-instance/001_onboot",
      "sudo mv /tmp/bitwarden-install.sh /etc/profile.d/bitwarden-install.sh",
      "sudo chown root:root /etc/update-motd.d/99-bitwarden-welcome /etc/ufw/applications.d/bitwarden /opt/bitwarden/install-bitwarden.sh /var/lib/cloud/scripts/per-instance/001_onboot /etc/profile.d/bitwarden-install.sh",
      "sudo chmod 644 /etc/ufw/applications.d/bitwarden /etc/profile.d/bitwarden-install.sh"
    ]
  }

  provisioner "shell" {
    environment_vars = [
      "DEBIAN_FRONTEND=noninteractive",
      "LC_ALL=C",
      "LANG=en_US.UTF-8",
      "LC_CTYPE=en_US.UTF-8"
    ]
    inline = [
      "sudo apt-get -qqy update",
      "sudo apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' full-upgrade",
      "sudo apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.apt_packages}",
      "sudo install -m 0755 -d /etc/apt/keyrings",
      "curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg",
      "sudo chmod a+r /etc/apt/keyrings/docker.gpg",
      "echo \"deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo \"$VERSION_CODENAME\") stable\" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null",
      "sudo apt-get -qqy update",
      "sudo apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.docker_packages}",
      "sudo apt-get -qqy clean"
    ]
  }

  provisioner "shell" {
    execute_command = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E bash '{{ .Path }}'"
    environment_vars = [
      "application_name=${var.application_name}",
      "application_version=${var.application_version}",
      "DEBIAN_FRONTEND=noninteractive",
      "LC_ALL=C",
      "LANG=en_US.UTF-8",
      "LC_CTYPE=en_US.UTF-8"
    ]
    scripts = [
      "scripts/01-setup-first-run.sh",
      "scripts/02-ufw-bitwarden.sh",
      "scripts/90-cleanup.sh",
      "scripts/99-img-check.sh"
    ]
  }

  # Azure generalization - must be the last provisioner
  provisioner "shell" {
    execute_command = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E sh '{{ .Path }}'"
    inline = [
      "/usr/sbin/waagent -force -deprovision+user && export HISTSIZE=0 && sync"
    ]
  }

  post-processor "manifest" {
    output     = "manifest.json"
    strip_path = true
  }
}
