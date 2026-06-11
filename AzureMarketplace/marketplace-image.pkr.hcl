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

# Pinned to a version above what apt noble-updates ships (currently 2.11.x).
# Azure Marketplace cert check 200.3.3.4 fails for unspecified reasons against
# the apt build; switch to a tarball install from upstream to remove the
# Canonical packaging as a variable.
variable "waagent_version" {
  type    = string
  default = "2.15.0.1"
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
  image_name = "bitwarden-24-04-${local.timestamp}"
}

source "azure-arm" "bitwarden_self_host" {
  use_azure_cli_auth = true
  subscription_id    = var.subscription_id

  os_type         = "Linux"
  image_publisher = "Canonical"
  image_offer     = "ubuntu-24_04-lts"
  image_sku       = "server"

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

  # Upload common files to /tmp staging area (azure-arm connects as a non-root user)
  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/update-motd.d/99-bitwarden-welcome"
    destination = "/tmp/99-bitwarden-welcome"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/ufw/applications.d/bitwarden"
    destination = "/tmp/bitwarden-ufw"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/opt/bitwarden/setup-wizard.sh"
    destination = "/tmp/setup-wizard.sh"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/opt/bitwarden/install-standard.sh"
    destination = "/tmp/install-standard.sh"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/opt/bitwarden/install-lite.sh"
    destination = "/tmp/install-lite.sh"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/var/lib/cloud/scripts/per-instance/001_onboot"
    destination = "/tmp/001_onboot"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/systemd/system/disable-swap.service"
    destination = "/tmp/disable-swap.service"
  }

  # Move staged files to their final system locations
  provisioner "shell" {
    inline = [
      "sudo mkdir -p /etc/update-motd.d /etc/ufw/applications.d /opt/bitwarden /var/lib/cloud/scripts/per-instance",
      "sudo mv /tmp/99-bitwarden-welcome /etc/update-motd.d/99-bitwarden-welcome",
      "sudo mv /tmp/bitwarden-ufw /etc/ufw/applications.d/bitwarden",
      "sudo mv /tmp/setup-wizard.sh /opt/bitwarden/setup-wizard.sh",
      "sudo mv /tmp/install-standard.sh /opt/bitwarden/install-standard.sh",
      "sudo mv /tmp/install-lite.sh /opt/bitwarden/install-lite.sh",
      "sudo mv /tmp/001_onboot /var/lib/cloud/scripts/per-instance/001_onboot",
      "sudo mv /tmp/disable-swap.service /etc/systemd/system/disable-swap.service",
      "sudo chown root:root /etc/update-motd.d/99-bitwarden-welcome /etc/ufw/applications.d/bitwarden /opt/bitwarden/setup-wizard.sh /opt/bitwarden/install-standard.sh /opt/bitwarden/install-lite.sh /var/lib/cloud/scripts/per-instance/001_onboot /etc/systemd/system/disable-swap.service",
      "sudo chmod 644 /etc/ufw/applications.d/bitwarden /etc/systemd/system/disable-swap.service",
      "sudo systemctl enable disable-swap.service"
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
    environment_vars = ["DEBIAN_FRONTEND=noninteractive"]
    inline = [
      # Remove any walinuxagent shipped by Canonical so apt's version cannot
      # override the upstream-source install on later upgrade.
      "sudo apt-get -qqy purge walinuxagent || true",
      "sudo apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install python3 python3-setuptools",
      # Fetch the pinned upstream release and install via setup.py. The
      # --register-service flag drops a systemd unit and a /etc/waagent.conf
      # if missing. var.waagent_version is interpolated at template-compile
      # time, not at shell-runtime.
      "curl -fsSL -o /tmp/walinuxagent.tar.gz https://github.com/Azure/WALinuxAgent/archive/refs/tags/v${var.waagent_version}.tar.gz",
      "sudo mkdir -p /opt/walinuxagent-src",
      "sudo tar -xzf /tmp/walinuxagent.tar.gz -C /opt/walinuxagent-src --strip-components=1",
      "cd /opt/walinuxagent-src && sudo python3 setup.py install --register-service",
      "sudo rm -f /tmp/walinuxagent.tar.gz",
      # Force systemd to re-read units after setup.py modifies walinuxagent.service.
      # The version systemd has loaded is outdated" on first boot.
      "sudo systemctl daemon-reload",
      "sudo systemctl enable walinuxagent",
      "waagent --version",
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
      "../CommonMarketplace/scripts/01-setup-first-run.sh",
      "../CommonMarketplace/scripts/02-ufw-bitwarden.sh",
      "../CommonMarketplace/scripts/90-cleanup.sh",
      "scripts/99-img-check.sh",
      "../CommonMarketplace/scripts/99-cleanup-final.sh"
    ]
  }

  # Azure-specific cleanup. First pass at history deletion runs here so the
  # image is in a clean state before deprovision; a second pass runs after
  # deprovision below to catch anything deprovision recreates.
  provisioner "shell" {
    execute_command  = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E bash '{{ .Path }}'"
    environment_vars = [
      "HISTFILE=/dev/null",
      "HISTSIZE=0",
    ]
    inline = [
      "truncate -s 0 /var/log/waagent.log 2>/dev/null || true",
      "find / -name '.bash_history' -type f -delete 2>/dev/null || true",
    ]
  }

  # Azure generalization - must be the last provisioner.
  # Runs `sh` (dash) which does not write bash history, but waagent itself
  # may shell out via bash during deprovision and recreate /root/.bash_history.
  # After deprovision finishes, sweep again with HISTFILE=/dev/null so the
  # captured disk has no .bash_history regardless of who recreated it.
  provisioner "shell" {
    execute_command = "chmod +x {{ .Path }}; {{ .Vars }} sudo -E sh '{{ .Path }}'"
    environment_vars = [
      "HISTFILE=/dev/null",
      "HISTSIZE=0",
    ]
    inline = [
      "/usr/sbin/waagent -force -deprovision+user && find / -name '.bash_history' -type f -delete 2>/dev/null; export HISTSIZE=0 && sync"
    ]
  }

  post-processor "manifest" {
    output     = "manifest.json"
    strip_path = true
  }
}
