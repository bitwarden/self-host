#!/bin/bash
#
# Install Bitwarden Standard
# ref: https://bitwarden.com/help/install-on-premise-linux/
#

echo -e ''
echo -e 'Downloading Bitwarden installer...'
echo -e ''

curl -L -s -o /home/bitwarden/bitwarden.sh \
    "https://func.bitwarden.com/api/dl/?app=self-host&platform=linux"
chmod +x /home/bitwarden/bitwarden.sh
chown bitwarden:bitwarden /home/bitwarden/bitwarden.sh

docker pull ghcr.io/bitwarden/setup

echo -e ''
echo -e 'Installing Bitwarden...'
echo -e ''

sudo -i -u bitwarden /home/bitwarden/bitwarden.sh install

echo -e ''
echo -e 'Starting Bitwarden containers...'
echo -e ''

sudo -i -u bitwarden /home/bitwarden/bitwarden.sh start

echo -e ''
echo -e 'Waiting for Bitwarden database container to come online...'

sleep 30s

echo -e 'Initializing Bitwarden database...'
echo -e ''

sudo -i -u bitwarden /home/bitwarden/bitwarden.sh updatedb

echo -e ''
echo -e 'Bitwarden installation complete.'
echo -e ''

#
# Setup Bitwarden update cron
# ref: https://bitwarden.com/help/updating-on-premise/
#

echo -e '#!/usr/bin/env bash\nsudo -i -u bitwarden /home/bitwarden/bitwarden.sh updateself\nsudo -i -u bitwarden /home/bitwarden/bitwarden.sh update' \
    > /etc/cron.weekly/bitwardenupdate

chmod +x /etc/cron.weekly/bitwardenupdate
