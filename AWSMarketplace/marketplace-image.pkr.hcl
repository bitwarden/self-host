packer {
  required_plugins {
    amazon = {
      version = ">= 1.2.0"
      source  = "github.com/hashicorp/amazon"
    }
  }
}

variable "application_name" {
  type    = string
  default = "Bitwarden"
}

variable "application_version" {
  type    = string
  default = "${env("AWS_IMG_VERSION")}"
}

variable "apt_packages" {
  type    = string
  default = "fail2ban ca-certificates curl gnupg"
}

variable "docker_packages" {
  type    = string
  default = "docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin"
}

variable "aws_region" {
  type    = string
  default = "us-east-1"
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

source "amazon-ebs" "bitwarden_self_host" {
  region        = var.aws_region
  instance_type = "t3.small"
  ssh_username  = "ubuntu"

  source_ami_filter {
    filters = {
      name                = "ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*"
      root-device-type    = "ebs"
      virtualization-type = "hvm"
    }
    most_recent = true
    owners      = ["099720109477"] # Canonical
  }

  launch_block_device_mappings {
    device_name           = "/dev/sda1"
    volume_size           = 32
    volume_type           = "gp3"
    delete_on_termination = true
  }

  ami_name        = local.image_name
  ami_description = "Bitwarden Self-Host ${var.application_version}"

  tags = {
    Name        = local.image_name
    Application = "bitwarden-packer-build"
    Version     = var.application_version
    GitHub_Run  = "github-run-${var.github_run_id}"
  }

  run_tags = {
    Name        = "packer-bitwarden-${var.github_run_id}"
    Application = "bitwarden-packer-build"
    GitHub_Run  = "github-run-${var.github_run_id}"
  }
}

build {
  sources = ["source.amazon-ebs.bitwarden_self_host"]

  provisioner "shell" {
    inline = ["cloud-init status --wait"]
  }

  # Upload common files to /tmp staging area (amazon-ebs connects as a non-root user)
  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/update-motd.d/99-bitwarden-welcome"
    destination = "/tmp/99-bitwarden-welcome"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/ufw/applications.d/bitwarden"
    destination = "/tmp/bitwarden-ufw"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/opt/bitwarden/install-bitwarden.sh"
    destination = "/tmp/install-bitwarden.sh"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/var/lib/cloud/scripts/per-instance/001_onboot"
    destination = "/tmp/001_onboot"
  }

  provisioner "file" {
    source      = "../CommonMarketplace/files/etc/profile.d/bitwarden-first-login.sh"
    destination = "/tmp/bitwarden-first-login.sh"
  }

  # Move staged files to their final system locations
  provisioner "shell" {
    inline = [
      "sudo mkdir -p /etc/update-motd.d /etc/ufw/applications.d /opt/bitwarden /var/lib/cloud/scripts/per-instance",
      "sudo mv /tmp/99-bitwarden-welcome /etc/update-motd.d/99-bitwarden-welcome",
      "sudo mv /tmp/bitwarden-ufw /etc/ufw/applications.d/bitwarden",
      "sudo mv /tmp/install-bitwarden.sh /opt/bitwarden/install-bitwarden.sh",
      "sudo mv /tmp/001_onboot /var/lib/cloud/scripts/per-instance/001_onboot",
      "sudo mv /tmp/bitwarden-first-login.sh /etc/profile.d/bitwarden-first-login.sh",
      "sudo chown root:root /etc/update-motd.d/99-bitwarden-welcome /etc/ufw/applications.d/bitwarden /opt/bitwarden/install-bitwarden.sh /var/lib/cloud/scripts/per-instance/001_onboot /etc/profile.d/bitwarden-first-login.sh",
      "sudo chmod 644 /etc/ufw/applications.d/bitwarden /etc/profile.d/bitwarden-first-login.sh"
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
      "../CommonMarketplace/scripts/01-setup-first-run.sh",
      "../CommonMarketplace/scripts/02-ufw-bitwarden.sh",
      "../CommonMarketplace/scripts/90-cleanup.sh",
      "scripts/99-img-check.sh"
    ]
  }

  post-processor "manifest" {
    output     = "manifest.json"
    strip_path = true
  }
}
