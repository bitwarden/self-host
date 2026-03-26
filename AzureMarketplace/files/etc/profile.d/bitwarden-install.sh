#!/bin/bash
#
# First-login trigger for Bitwarden installation.
# This script runs once on the first interactive login, then removes itself.
# Skip for the bitwarden service account (it doesn't have sudo).

if [ "$(whoami)" = "bitwarden" ]; then
  return 0 2>/dev/null || exit 0
fi

if [ -f /opt/bitwarden/install-bitwarden.sh ]; then
  # Wait for cloud-init to finish (downloads bitwarden.sh on first boot)
  echo "Waiting for cloud-init to complete..."
  sudo cloud-init status --wait > /dev/null 2>&1
  sudo /opt/bitwarden/install-bitwarden.sh
fi
