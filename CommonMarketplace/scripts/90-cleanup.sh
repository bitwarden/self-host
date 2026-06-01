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
# Write to BOTH the main sshd_config and a drop-in:
#   - Main file: satisfies Azure's certification probe, which appears to do a
#     literal grep of /etc/ssh/sshd_config and does not honor Include'd drop-ins.
#   - Drop-in:   wins at sshd runtime against any later cloud-init drop-in
#     because /etc/ssh/sshd_config.d/*.conf is sourced in lexical order and
#     sshd uses the first occurrence of each directive.
for directive in "ClientAliveInterval 120" "ClientAliveCountMax 3"; do
  key="${directive%% *}"
  if grep -qE "^[#[:space:]]*${key}\b" /etc/ssh/sshd_config; then
    sed -i "s|^[#[:space:]]*${key}\b.*|${directive}|" /etc/ssh/sshd_config
  else
    echo "${directive}" >> /etc/ssh/sshd_config
  fi
done

cat > /etc/ssh/sshd_config.d/10-bitwarden-marketplace.conf <<'EOF'
ClientAliveInterval 120
ClientAliveCountMax 3
EOF
chmod 644 /etc/ssh/sshd_config.d/10-bitwarden-marketplace.conf

rm -rf /tmp/* /var/tmp/*

# Clear bash history for all users
unset HISTFILE
export HISTSIZE=0
for home_dir in /root /home/*; do
  if [ -d "$home_dir" ]; then
    rm -f "$home_dir/.bash_history" 2>/dev/null || true
  fi
done

find /var/log -mtime -1 -type f -exec truncate -s 0 {} \;
rm -rf /var/log/*.gz /var/log/*.[0-9] /var/log/*-????????
# Reset cloud-init state for redeploy. `rm -rf /var/lib/cloud/instances/*` alone
# leaves the /var/lib/cloud/instance symlink dangling, which causes cloud-init
# to enter `degraded done` on the deployed VM's first boot
if command -v cloud-init >/dev/null 2>&1; then
  cloud-init clean --logs --machine-id
fi
rm -rf /var/lib/cloud/instance /var/lib/cloud/instances/* /var/lib/cloud/data/* /var/lib/cloud/sem/*
rm -f /root/.ssh/authorized_keys /home/ubuntu/.ssh/authorized_keys
