packer {
  required_plugins {
    digitalocean = {
      version = ">= 1.0.4"
      source  = "github.com/digitalocean/digitalocean"
    }
  }
}

variable "application_name" {
  type    = string
  default = "Bitwarden"
}

variable "application_version" {
  type    = string
  default = "${env("DIGITALOCEAN_IMG_VERSION")}"
}

variable "apt_packages" {
  type    = string
  default = "fail2ban ca-certificates curl gnupg"
}

variable "docker_packages" {
  type    = string
  default = "docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin"
}

variable "do_token" {
  type      = string
  default   = "${env("DIGITALOCEAN_TOKEN")}"
  sensitive = true
}

# "timestamp" template function replacement
locals { timestamp = regex_replace(timestamp(), "[- TZ:]", "") }

# All locals variables are generated from variables that uses expressions
# that are not allowed in HCL2 variables.
locals {
  image_name = "bitwarden-22-04-snapshot-${local.timestamp}"
}

source "digitalocean" "bitwarden_self_host" {
  api_token     = "${var.do_token}"
  image         = "ubuntu-22-04-x64"
  region        = "nyc3"
  size          = "s-1vcpu-1gb"
  snapshot_name = "${local.image_name}"
  ssh_username  = "root"
}

build {
  sources = ["source.digitalocean.bitwarden_self_host"]

  provisioner "shell" {
    inline = ["cloud-init status --wait"]
  }

  provisioner "file" {
    destination = "/etc/"
    source      = "files/etc/"
  }

  provisioner "file" {
    destination = "/opt/"
    source      = "files/opt/"
  }

  provisioner "file" {
    destination = "/var/"
    source      = "files/var/"
  }

  provisioner "shell" {
    environment_vars = [
      "DEBIAN_FRONTEND=noninteractive",
      "LC_ALL=C",
      "LANG=en_US.UTF-8",
      "LC_CTYPE=en_US.UTF-8"
    ]
    inline           = [
      "apt-get -qqy update",
      "apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' full-upgrade",
      "apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.apt_packages}",
      "install -m 0755 -d /etc/apt/keyrings",
      "curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg",
      "chmod a+r /etc/apt/keyrings/docker.gpg",
      "echo \"deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo \"$VERSION_CODENAME\") stable\" | tee /etc/apt/sources.list.d/docker.list > /dev/null",
      "apt-get -qqy update",
      "apt-get -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.docker_packages}",
      "apt-get -qqy clean",
      "rm -rf /opt/digitalocean",
      "rm -rf /var/log/auth.log",
      "rm -rf /var/log/kern.log",
      "rm -rf /var/log/ufw.log",
      "rm -rf /var/log/ubuntu-advantage.log",
      "rm -rf /var/log/droplet-agent.update.log"
    ]
  }

  provisioner "shell" {
    environment_vars = [
      "application_name=${var.application_name}",
      "application_version=${var.application_version}",
      "DEBIAN_FRONTEND=noninteractive",
      "LC_ALL=C",
      "LANG=en_US.UTF-8",
      "LC_CTYPE=en_US.UTF-8"
    ]
    scripts          = [
      "scripts/01-setup-first-run.sh",
      "scripts/02-ufw-bitwarden.sh",
      "scripts/03-force-ssh-logout.sh",
      "scripts/90-cleanup.sh",
      "scripts/99-img-check.sh"
    ]
  }

  post-processor "manifest" {
    output     = "manifest.json"
    strip_path = true
  }
}
