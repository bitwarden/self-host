#!/bin/bash

# Azure Marketplace Image Validation Tool

# Prevent this script from writing to bash history
unset HISTFILE
export HISTSIZE=0

VERSION="v. 1.0.0"
RUNDATE=$( date )

# Script should be run with SUDO
if [ "$EUID" -ne 0 ]
  then echo "[Error] - This script must be run with sudo or as the root user."
  exit 1
fi

STATUS=0
PASS=0
WARN=0
FAIL=0

echo "Azure Marketplace Image Validation Tool ${VERSION}"
echo "Executed on: ${RUNDATE}"
echo "Checking local system for Marketplace compatibility..."
echo ""

# Check OS
if [ -f /etc/os-release ]; then
  . /etc/os-release
  OS=$NAME
  VER=$VERSION_ID
else
  OS=$(uname -s)
  VER=$(uname -r)
fi

echo -en "Distribution: ${OS}\n"
echo -en "Version: ${VER}\n\n"

if [[ $OS == "Ubuntu" ]] && [[ $VER == "24.04" ]]; then
  echo -en "\e[32m[PASS]\e[0m Supported OS detected: ${OS} ${VER}\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m ${OS} ${VER} is not the expected OS (Ubuntu 24.04)\n"
  ((FAIL++))
  STATUS=2
fi

# Check cloud-init
if hash cloud-init 2>/dev/null; then
  echo -en "\e[32m[PASS]\e[0m Cloud-init is installed.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Cloud-init is not installed.\n"
  ((FAIL++))
  STATUS=2
fi

# Check Azure Linux Agent version (minimum 2.7.x required)
if hash waagent 2>/dev/null; then
  WAAGENT_VERSION=$(waagent --version 2>&1 | head -1 | grep -oP '(?<=WALinuxAgent-)\d+\.\d+' | head -1)
  WAAGENT_MAJOR=$(echo "${WAAGENT_VERSION}" | cut -d. -f1)
  WAAGENT_MINOR=$(echo "${WAAGENT_VERSION}" | cut -d. -f2)
  if [[ "${WAAGENT_MAJOR}" -gt 2 ]] || ([[ "${WAAGENT_MAJOR}" -eq 2 ]] && [[ "${WAAGENT_MINOR}" -ge 7 ]]); then
    echo -en "\e[32m[PASS]\e[0m Azure Linux Agent version ${WAAGENT_VERSION} meets minimum requirement.\n"
    ((PASS++))
  else
    echo -en "\e[41m[FAIL]\e[0m Azure Linux Agent version ${WAAGENT_VERSION} is below minimum supported version (2.7.x).\n"
    ((FAIL++))
    STATUS=2
  fi
else
  echo -en "\e[41m[FAIL]\e[0m Azure Linux Agent (waagent) is not installed.\n"
  ((FAIL++))
  STATUS=2
fi

# Check Azure Linux Agent service is enabled (will start on the deployed VM)
if systemctl is-enabled walinuxagent >/dev/null 2>&1; then
  echo -en "\e[32m[PASS]\e[0m Azure Linux Agent service is enabled.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Azure Linux Agent service is not enabled.\n"
  ((FAIL++))
  STATUS=2
fi

# Check Docker
if hash docker 2>/dev/null; then
  echo -en "\e[32m[PASS]\e[0m Docker is installed.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Docker is not installed.\n"
  ((FAIL++))
  STATUS=2
fi

# Check docker compose plugin
if docker compose version > /dev/null 2>&1; then
  echo -en "\e[32m[PASS]\e[0m Docker Compose plugin is installed.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Docker Compose plugin is not installed.\n"
  ((FAIL++))
  STATUS=2
fi

# Check firewall
if [[ $OS == "Ubuntu" ]]; then
  ufwa=$(ufw status | head -1 | sed -e "s/^Status:\ //")
  if [[ $ufwa == "active" ]]; then
    echo -en "\e[32m[PASS]\e[0m Firewall (ufw) is active.\n"
    ((PASS++))
  else
    echo -en "\e[93m[WARN]\e[0m Firewall (ufw) is not active.\n"
    ((WARN++))
    if [[ $STATUS != 2 ]]; then STATUS=1; fi
  fi
fi

# Check root password
SHADOW=$(cat /etc/shadow)
for usr in $SHADOW; do
  IFS=':' read -r -a u <<< "$usr"
  if [[ "${u[0]}" == "root" ]]; then
    if [[ ${u[1]} == "!" ]] || [[ ${u[1]} == "!!" ]] || [[ ${u[1]} == "*" ]]; then
      echo -en "\e[32m[PASS]\e[0m Root user has no password set.\n"
      ((PASS++))
    else
      echo -en "\e[41m[FAIL]\e[0m Root user has a password set.\n"
      ((FAIL++))
      STATUS=2
    fi
  fi
done

# Check SSH keys
if [ -f /root/.ssh/authorized_keys ] && [ "$(wc -c < /root/.ssh/authorized_keys)" -gt 50 ]; then
  echo -en "\e[41m[FAIL]\e[0m Root has a populated authorized_keys file.\n"
  ((FAIL++))
  STATUS=2
else
  echo -en "\e[32m[PASS]\e[0m No SSH keys found for root.\n"
  ((PASS++))
fi

if [ -f /home/ubuntu/.ssh/authorized_keys ] && [ "$(wc -c < /home/ubuntu/.ssh/authorized_keys)" -gt 50 ]; then
  echo -en "\e[41m[FAIL]\e[0m Ubuntu user has a populated authorized_keys file.\n"
  ((FAIL++))
  STATUS=2
else
  echo -en "\e[32m[PASS]\e[0m No SSH keys found for ubuntu user.\n"
  ((PASS++))
fi

# Check SSH ClientAliveInterval (Azure requirement: 30-235 seconds)
ALIVE_INTERVAL=$(sshd -T -C user=root,host=localhost,addr=127.0.0.1 2>/dev/null \
  | awk '/^clientaliveinterval/{print $2}')
if [[ -n "${ALIVE_INTERVAL}" ]] && [[ "${ALIVE_INTERVAL}" -ge 30 ]] && [[ "${ALIVE_INTERVAL}" -le 235 ]]; then
  echo -en "\e[32m[PASS]\e[0m SSH ClientAliveInterval is ${ALIVE_INTERVAL} seconds (30-235 required).\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m SSH ClientAliveInterval is not configured in the required range of 30-235 seconds (got: ${ALIVE_INTERVAL:-not set}).\n"
  ((FAIL++))
  STATUS=2
fi

# Check no swap on OS disk
SWAP_ACTIVE=$(swapon --show 2>/dev/null)
if [[ -z "${SWAP_ACTIVE}" ]]; then
  echo -en "\e[32m[PASS]\e[0m No active swap partitions detected.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Swap is active. Disable swap before submitting to Azure Marketplace.\n"
  ((FAIL++))
  STATUS=2
fi

# Check waagent will not recreate swap on first boot of the deployed VM.
if [ -f /etc/waagent.conf ] && grep -q "^ResourceDisk.EnableSwap=n" /etc/waagent.conf; then
  echo -en "\e[32m[PASS]\e[0m waagent ResourceDisk.EnableSwap is disabled.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m waagent will recreate swap on first boot (ResourceDisk.EnableSwap is not 'n').\n"
  ((FAIL++))
  STATUS=2
fi

# Check bash history — Azure tests for file existence, not size.
if [ ! -f /root/.bash_history ] && [ ! -f /home/ubuntu/.bash_history ]; then
  echo -en "\e[32m[PASS]\e[0m No bash history files present.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m bash history file present (must be deleted, not truncated).\n"
  ((FAIL++))
  STATUS=2
fi

# Check cloud-init first-boot script is present and executable
if [ -x /var/lib/cloud/scripts/per-instance/001_onboot ]; then
  echo -en "\e[32m[PASS]\e[0m Cloud-init first-boot script is present and executable.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Cloud-init first-boot script not found at /var/lib/cloud/scripts/per-instance/001_onboot.\n"
  ((FAIL++))
  STATUS=2
fi

# Check for log files
echo -en "\nChecking for log files in /var/log\n"
for f in /var/log/*-????????; do
  [[ -e $f ]] || break
  echo -en "\e[93m[WARN]\e[0m Log archive ${f} found.\n"
  ((WARN++))
  if [[ $STATUS != 2 ]]; then STATUS=1; fi
done
for f in /var/log/*.[0-9]; do
  [[ -e $f ]] || break
  echo -en "\e[93m[WARN]\e[0m Log archive ${f} found.\n"
  ((WARN++))
  if [[ $STATUS != 2 ]]; then STATUS=1; fi
done

# Summary
echo -en "\n---------------------------------------------------------------------------------------------------\n"

if [[ $STATUS == 0 ]]; then
  echo -en "Scan Complete.\n\e[32mAll Tests Passed!\e[0m\n"
elif [[ $STATUS == 1 ]]; then
  echo -en "Scan Complete.\n\e[93mSome non-critical tests failed. Please review these items.\e[0m\n"
else
  echo -en "Scan Complete.\n\e[41mOne or more tests failed. Please review these items and re-test.\e[0m\n"
fi
echo "---------------------------------------------------------------------------------------------------"
echo -en "\e[1m${PASS} Tests PASSED\e[0m\n"
echo -en "\e[1m${WARN} WARNINGS\e[0m\n"
echo -en "\e[1m${FAIL} Tests FAILED\e[0m\n"
echo -en "---------------------------------------------------------------------------------------------------\n"

if [[ $STATUS == 0 ]]; then
  echo -en "No issues detected. Ensure all software is functional, secure, and properly configured.\n\n"
  exit 0
elif [[ $STATUS == 1 ]]; then
  echo -en "Please review all [WARN] items above and ensure they are intended or resolved.\n\n"
  exit 0
else
  echo -en "Critical tests failed. These must be resolved before submitting to Azure Marketplace.\n\n"
  exit 1
fi
