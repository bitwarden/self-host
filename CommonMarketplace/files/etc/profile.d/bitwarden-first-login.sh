#!/bin/bash
#
# First-login trigger for Bitwarden installation.
# This script runs once on the first interactive login, then removes itself.
# Skip for the bitwarden service account (it doesn't have sudo).

if [ "$(whoami)" = "bitwarden" ]; then
  return 0 2>/dev/null || exit 0
fi

if [ -f /opt/bitwarden/setup-wizard.sh ]; then
  # Wait for cloud-init to finish before running the setup wizard
  echo "Waiting for cloud-init to complete..."
  sudo cloud-init status --wait > /dev/null 2>&1
  sudo /opt/bitwarden/setup-wizard.sh
fi
