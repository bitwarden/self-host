#!/bin/bash

# Marketplace Image Cleanup

set -o errexit

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

# Disable swap (marketplace requirement: no swap on OS disk)
swapoff -a 2>/dev/null || true
sed -i '/\bswap\b/d' /etc/fstab
if [ -f /swapfile ]; then
  rm -f /swapfile
fi

# Configure SSH client alive interval (Azure requirement: 30-235 seconds)
if grep -q "^#*\s*ClientAliveInterval" /etc/ssh/sshd_config; then
  sed -i 's/^#*\s*ClientAliveInterval.*/ClientAliveInterval 120/' /etc/ssh/sshd_config
else
  echo "ClientAliveInterval 120" >> /etc/ssh/sshd_config
fi

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
