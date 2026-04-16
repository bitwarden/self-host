#!/bin/bash
#
# Install Bitwarden Lite
# ref: https://bitwarden.com/help/install-and-deploy-lite/
#

# Generate a random database password
DB_PASSWORD=$(openssl rand -hex 16)

echo -e ''
echo -e 'Downloading Bitwarden Lite configuration...'
echo -e ''

# Download docker-compose.yml
curl -L -s -o /home/bitwarden/docker-compose.yml \
    "https://raw.githubusercontent.com/bitwarden/self-host/main/bitwarden-lite/docker-compose.yml"

# Download settings.env and set the generated database password
curl -L -s -o /home/bitwarden/settings.env \
    "https://raw.githubusercontent.com/bitwarden/self-host/main/bitwarden-lite/settings.env"

sed -i "s|^BW_DB_PASSWORD=.*|BW_DB_PASSWORD=${DB_PASSWORD}|" /home/bitwarden/settings.env

# Update the MariaDB container password in docker-compose.yml to match
sed -i "s|MARIADB_PASSWORD: \"super_strong_password\"|MARIADB_PASSWORD: \"${DB_PASSWORD}\"|" \
    /home/bitwarden/docker-compose.yml

chmod 600 /home/bitwarden/settings.env
chown bitwarden:bitwarden /home/bitwarden/docker-compose.yml /home/bitwarden/settings.env

echo -e ''
echo -e 'Starting Bitwarden Lite...'
echo -e ''

# Start Bitwarden Lite. Database migrations run automatically on first boot.
cd /home/bitwarden && docker compose up -d

echo -e ''
echo -e 'Bitwarden Lite is running.'
echo -e ''
echo -e 'Next steps:'
echo -e '  1. Edit /home/bitwarden/settings.env to set BW_DOMAIN, BW_INSTALLATION_ID,'
echo -e '     and BW_INSTALLATION_KEY.'
echo -e '  2. Get installation credentials at: https://bitwarden.com/host/'
echo -e '  3. Restart services: cd /home/bitwarden && docker compose up -d'
echo -e ''

#
# Setup Bitwarden Lite update cron
# ref: https://bitwarden.com/help/install-and-deploy-lite/
#

printf '#!/usr/bin/env bash\ncd /home/bitwarden && docker compose pull && docker compose up -d\n' \
    > /etc/cron.weekly/bitwardenupdate
chmod +x /etc/cron.weekly/bitwardenupdate
