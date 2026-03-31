#!/bin/bash

# Azure Marketplace Image Validation Tool

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

clear
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

# Check Azure Linux Agent
if hash waagent 2>/dev/null; then
  echo -en "\e[32m[PASS]\e[0m Azure Linux Agent (waagent) is installed.\n"
  ((PASS++))
else
  echo -en "\e[41m[FAIL]\e[0m Azure Linux Agent (waagent) is not installed.\n"
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

# Check bash history
if [ -f /root/.bash_history ]; then
  BH_S=$(wc -c < /root/.bash_history)
  if [[ $BH_S -lt 200 ]]; then
    echo -en "\e[32m[PASS]\e[0m Root bash history appears cleared.\n"
    ((PASS++))
  else
    echo -en "\e[41m[FAIL]\e[0m Root bash history should be cleared.\n"
    ((FAIL++))
    STATUS=2
  fi
else
  echo -en "\e[32m[PASS]\e[0m Root bash history is not present.\n"
  ((PASS++))
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
