#!/bin/bash
#
# Bitwarden Self-Host Setup Wizard
# Runs once on first interactive login to select the deployment edition.
#

cat <<'EOF'

################################################################################

  Welcome to Bitwarden Self-Host!

  Select your deployment edition:

    1) Standard
       Full multi-container deployment with all Bitwarden services.
       Recommended for production use and larger teams.
       Docs: https://bitwarden.com/help/install-on-premise-linux/

    2) Lite
       Lightweight single-container deployment.
       Designed for individuals and small teams with lower resource needs.
       Docs: https://bitwarden.com/help/install-and-deploy-lite/

  Not sure which to choose?
    https://bitwarden.com/help/self-host-an-organization/

################################################################################

EOF

while true; do
    read -rp "  Enter selection [1 or 2]: " SELECTION
    case "$SELECTION" in
        1)
            EDITION="standard"
            break
            ;;
        2)
            EDITION="lite"
            break
            ;;
        *)
            echo "  Please enter 1 or 2."
            ;;
    esac
done

echo ""
echo "  Edition selected: $EDITION"
echo ""

# Persist the selected edition so MOTD and future scripts can read it
echo "$EDITION" > /home/bitwarden/.bw-edition
chown bitwarden:bitwarden /home/bitwarden/.bw-edition
chmod 600 /home/bitwarden/.bw-edition

# Run the appropriate installer
if [ "$EDITION" = "standard" ]; then
    /opt/bitwarden/install-standard.sh
else
    /opt/bitwarden/install-lite.sh
fi

# Remove the first-login trigger so this wizard doesn't run again
rm -f /etc/profile.d/bitwarden-first-login.sh
