#!/bin/bash

# Marketplace Image Cleanup

set -o errexit

# Prevent this script from writing to bash history
unset HISTFILE
export HISTSIZE=0

# Ensure /tmp exists and has the proper permissions
if [ ! -d /tmp ]; then
  mkdir /tmp
fi
chmod 1777 /tmp

if [ -n "$(command -v apt-get)" ]; then
  export DEBIAN_FRONTEND=noninteractive
  apt-get -y update
  apt-get -o Dpkg::Options::="--force-confold" upgrade -q -y
  apt-get -y autoremove
  apt-get -y autoclean
fi

# Disable swap (marketplace requirement: no swap on OS disk).
# Build-time: clear current swap and fstab.
swapoff -a 2>/dev/null || true
sed -i '/\bswap\b/d' /etc/fstab
if [ -f /swapfile ]; then
  rm -f /swapfile
fi
# Boot-time: tell waagent not to create resource-disk swap on first boot.
if [ -f /etc/waagent.conf ]; then
  sed -i 's/^ResourceDisk\.EnableSwap=.*/ResourceDisk.EnableSwap=n/' /etc/waagent.conf
  sed -i 's/^ResourceDisk\.SwapSizeMB=.*/ResourceDisk.SwapSizeMB=0/' /etc/waagent.conf
fi
# Boot-time: tell cloud-init not to create /swap.img.
cat > /etc/cloud/cloud.cfg.d/99-disable-swap.cfg <<'EOF'
swap:
  filename: /swap.img
  size: 0
  maxsize: 0
EOF
chmod 644 /etc/cloud/cloud.cfg.d/99-disable-swap.cfg

# Configure SSH client alive interval (Azure requirement: 30-235 seconds).
# Use a drop-in that sorts before /etc/ssh/sshd_config.d/50-cloud-init.conf so
# this setting wins — sshd uses the first occurrence of each directive.
cat > /etc/ssh/sshd_config.d/10-azure-marketplace.conf <<'EOF'
ClientAliveInterval 120
ClientAliveCountMax 3
EOF
chmod 644 /etc/ssh/sshd_config.d/10-azure-marketplace.conf

rm -rf /tmp/* /var/tmp/*

# Clear bash history for all users
unset HISTFILE
export HISTSIZE=0
for home_dir in /root /home/*; do
  if [ -d "$home_dir" ]; then
    cat /dev/null > "$home_dir/.bash_history" 2>/dev/null || true
  fi
done

find /var/log -mtime -1 -type f -exec truncate -s 0 {} \;
rm -rf /var/log/*.gz /var/log/*.[0-9] /var/log/*-????????
rm -rf /var/lib/cloud/instances/*
rm -f /root/.ssh/authorized_keys /home/ubuntu/.ssh/authorized_keys /etc/ssh/*key*
touch /etc/ssh/revoked_keys
chmod 600 /etc/ssh/revoked_keys
