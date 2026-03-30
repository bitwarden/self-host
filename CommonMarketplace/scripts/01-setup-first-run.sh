#!/bin/bash
#
# Scripts in this directory are run during the build process.
# Each script will be uploaded to /tmp on your build server,
# given execute permissions and run. The cleanup process will
# remove the scripts from your build system after they have run
# if you use the build_image task.
#

#
# Create dedicated bitwarden user with Docker access
#

useradd -m -s /bin/bash bitwarden
usermod -aG docker bitwarden

#
# Make MOTD and boot script executable
#

chmod +x /var/lib/cloud/scripts/per-instance/001_onboot

chmod +x /etc/update-motd.d/99-bitwarden-welcome

#
# Setup First Run Script
#

chmod +x /opt/bitwarden/install-bitwarden.sh
