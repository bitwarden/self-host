#!/bin/bash

#
# Install Bitwarden
# ref: https://help.bitwarden.com/article/install-on-premise/
#

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
# ref: https://help.bitwarden.com/article/updating-on-premise/
#

echo -e '#!/usr/bin/env bash\nsudo -i -u bitwarden /home/bitwarden/bitwarden.sh updateself\nsudo -i -u bitwarden /home/bitwarden/bitwarden.sh update' \
    > /etc/cron.weekly/bitwardenupdate

chmod +x /etc/cron.weekly/bitwardenupdate

#
# Cleanup - remove the login trigger
#

rm -f /etc/profile.d/bitwarden-first-login.sh
