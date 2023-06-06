packer {
  required_plugins {
    digitalocean = {
      version = ">= 1.0.4"
      source  = "github.com/digitalocean/digitalocean"
    }
  }
}

# This file was autogenerated by the 'packer hcl2_upgrade' command. We
# recommend double checking that everything is correct before going forward. We
# also recommend treating this file as disposable. The HCL2 blocks in this
# file can be moved to other files. For example, the variable blocks could be
# moved to their own 'variables.pkr.hcl' file, etc. Those files need to be
# suffixed with '.pkr.hcl' to be visible to Packer. To use multiple files at
# once they also need to be in the same folder. 'packer inspect folder/'
# will describe to you what is in that folder.

# Avoid mixing go templating calls ( for example ```{{ upper(`string`) }}``` )
# and HCL2 calls (for example '${ var.string_value_example }' ). They won't be
# executed together and the outcome will be unknown.

# All generated input variables will be of 'string' type as this is how Packer JSON
# views them; you can change their type later on. Read the variables type
# constraints documentation
# https://www.packer.io/docs/templates/hcl_templates/variables#type-constraints for more info.
variable "application_name" {
  type    = string
  default = "Bitwarden"
}

variable "application_version" {
  type    = string
  default = "${env("DIGITALOCEAN_IMG_VERSION")}"
}

variable "apt_packages_1" {
  type    = string
  default = "fail2ban ca-certificates curl gnupg lsb-release"
}

variable "apt_packages_2" {
  type    = string
  default = "docker-ce docker-ce-cli containerd.io"
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
# Read the documentation for locals blocks here:
# https://www.packer.io/docs/templates/hcl_templates/blocks/locals
locals {
  image_name = "bitwarden-20-04-snapshot-${local.timestamp}"
}

# source blocks are generated from your builders; a source can be referenced in
# build blocks. A build block runs provisioner and post-processors on a
# source. Read the documentation for source blocks here:
# https://www.packer.io/docs/templates/hcl_templates/blocks/source
source "digitalocean" "autogenerated_1" {
  api_token     = "${var.do_token}"
  image         = "ubuntu-20-04-x64"
  region        = "nyc3"
  size          = "s-1vcpu-1gb"
  snapshot_name = "${local.image_name}"
  ssh_username  = "root"
}

# a build block invokes sources and runs provisioning steps on them. The
# documentation for build blocks can be found here:
# https://www.packer.io/docs/templates/hcl_templates/blocks/build
build {
  sources = ["source.digitalocean.autogenerated_1"]

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
    environment_vars = ["DEBIAN_FRONTEND=noninteractive", "LC_ALL=C", "LANG=en_US.UTF-8", "LC_CTYPE=en_US.UTF-8"]
    inline           = ["apt -qqy update", "apt -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' full-upgrade", "apt -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.apt_packages_1}", "curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /usr/share/keyrings/docker-archive-keyring.gpg", "echo \"deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/docker-archive-keyring.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable\" | tee /etc/apt/sources.list.d/docker.list > /dev/null", "apt -qqy update", "apt -qqy -o Dpkg::Options::='--force-confdef' -o Dpkg::Options::='--force-confold' install ${var.apt_packages_2}", "apt -qqy clean", "curl -L \"https://github.com/docker/compose/releases/download/1.29.2/docker-compose-$(uname -s)-$(uname -m)\" -o /usr/local/bin/docker-compose", "chmod +x /usr/local/bin/docker-compose", "rm -rf /opt/digitalocean", "echo > /var/log/auth.log", "echo > /var/log/kern.log", "echo > /var/log/ufw.log"]
  }

  provisioner "shell" {
    environment_vars = ["application_name=${var.application_name}", "application_version=${var.application_version}", "DEBIAN_FRONTEND=noninteractive", "LC_ALL=C", "LANG=en_US.UTF-8", "LC_CTYPE=en_US.UTF-8"]
    scripts          = ["scripts/01-setup-first-run.sh", "scripts/02-ufw-bitwarden.sh", "scripts/03-force-ssh-logout.sh", "scripts/90-cleanup.sh", "scripts/99-img-check.sh"]
  }

  post-processor "manifest" {
    output     = "manifest.json"
    strip_path = true
  }
}
