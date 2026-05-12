#!/bin/bash

# Marketplace Image Final Cleanup
#
# Destructive operations that must run AFTER the validator (99-img-check.sh).
# Host-key removal in particular breaks `sshd -T`, which the validator uses to
# read the effective sshd config. Keep this script destructive-only; config
# writes belong in 90-cleanup.sh.

set -o errexit

# Prevent this script from writing to bash history
unset HISTFILE
export HISTSIZE=0

# Remove sshd host keys so each marketplace customer's VM regenerates unique
# keys on first boot. revoked_keys must exist or sshd will fail to start.
rm -f /etc/ssh/ssh_host_*
touch /etc/ssh/revoked_keys
chmod 600 /etc/ssh/revoked_keys
