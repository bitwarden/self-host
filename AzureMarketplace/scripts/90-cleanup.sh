#!/bin/bash

# Azure Marketplace Image Cleanup

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

rm -rf /tmp/* /var/tmp/*
cat /dev/null > /root/.bash_history
unset HISTFILE
find /var/log -mtime -1 -type f -exec truncate -s 0 {} \;
rm -rf /var/log/*.gz /var/log/*.[0-9] /var/log/*-????????
rm -rf /var/lib/cloud/instances/*
rm -f /root/.ssh/authorized_keys /home/ubuntu/.ssh/authorized_keys /etc/ssh/*key*
touch /etc/ssh/revoked_keys
chmod 600 /etc/ssh/revoked_keys

# Clear waagent logs
truncate -s 0 /var/log/waagent.log 2>/dev/null || true
