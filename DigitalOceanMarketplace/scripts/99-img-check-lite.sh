#!/bin/bash

# DigitalOcean Marketplace Image Validation Tool - Bitwarden Lite
# © 2021-2022 DigitalOcean LLC.
# This code is licensed under Apache 2.0 license (see LICENSE.md for details)

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

# $1 == command to check for
# returns: 0 == true, 1 == false
cmdExists() {
    if command -v "$1" > /dev/null 2>&1; then
        return 0
    else
        return 1
    fi
}

function getDistro {
    if [ -f /etc/os-release ]; then
    # shellcheck disable=SC1091
    . /etc/os-release
    OS=$NAME
    VER=$VERSION_ID
elif type lsb_release >/dev/null 2>&1; then
    OS=$(lsb_release -si)
    VER=$(lsb_release -sr)
elif [ -f /etc/lsb-release ]; then
    # shellcheck disable=SC1091
    . /etc/lsb-release
    OS=$DISTRIB_ID
    VER=$DISTRIB_RELEASE
elif [ -f /etc/debian_version ]; then
    OS=Debian
    VER=$(cat /etc/debian_version)
else
    OS=$(uname -s)
    VER=$(uname -r)
fi
}

function loadPasswords {
SHADOW=$(cat /etc/shadow)
}

function checkAgent {
  if [ -d /opt/digitalocean ];then
     echo -en "\e[41m[FAIL]\e[0m DigitalOcean directory detected.\n"
            ((FAIL++))
            STATUS=2
      if [[ $OS == "Ubuntu" ]] || [[ $OS == "Debian" ]]; then
        echo "To uninstall the agent and remove the DO directory: 'sudo apt-get purge droplet-agent'"
      fi
  else
    echo -en "\e[32m[PASS]\e[0m DigitalOcean Monitoring agent was not found\n"
    ((PASS++))
  fi
}

function checkLogs {
    echo -en "\nChecking for log files in /var/log\n\n"
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
    for f in /var/log/*.log; do
      [[ -e $f ]] || break
      if [[ "$(wc -c < "${f}")" -gt 50 ]]; then
        echo -en "\e[93m[WARN]\e[0m un-cleared log file, ${f} found.\n"
        ((WARN++))
        if [[ $STATUS != 2 ]]; then STATUS=1; fi
      fi
    done
}

function checkRoot {
    user="root"
    uhome="/root"
    for usr in $SHADOW
    do
      IFS=':' read -r -a u <<< "$usr"
      if [[ "${u[0]}" == "${user}" ]]; then
        if [[ ${u[1]} == "!" ]] || [[ ${u[1]} == "!!" ]] || [[ ${u[1]} == "*" ]]; then
            echo -en "\e[32m[PASS]\e[0m User ${user} has no password set.\n"
            ((PASS++))
        else
            echo -en "\e[41m[FAIL]\e[0m User ${user} has a password set on their account.\n"
            ((FAIL++))
            STATUS=2
        fi
      fi
    done
    if [ -d ${uhome}/ ]; then
        if [ -d ${uhome}/.ssh/ ]; then
            if ls ${uhome}/.ssh/* > /dev/null 2>&1; then
                for key in "${uhome}"/.ssh/*
                    do
                        if [ "${key}" == "${uhome}/.ssh/authorized_keys" ]; then
                            if [ "$(wc -c < "${key}")" -gt 50 ]; then
                                echo -en "\e[41m[FAIL]\e[0m User \e[1m${user}\e[0m has a populated authorized_keys file in \e[93m${key}\e[0m\n"
                                ((FAIL++))
                                STATUS=2
                            fi
                        fi
                    done
            else
                echo -en "\e[32m[ OK ]\e[0m User \e[1m${user}\e[0m has no SSH keys present\n"
            fi
        else
            echo -en "\e[32m[ OK ]\e[0m User \e[1m${user}\e[0m does not have an .ssh directory\n"
        fi
        if [ -f /root/.bash_history ]; then
            BH_S=$(wc -c < /root/.bash_history)
            if [[ $BH_S -lt 200 ]]; then
                echo -en "\e[32m[PASS]\e[0m ${user}'s Bash History appears to have been cleared\n"
                ((PASS++))
            else
                echo -en "\e[41m[FAIL]\e[0m ${user}'s Bash History should be cleared to prevent sensitive information from leaking\n"
                ((FAIL++))
                STATUS=2
            fi
        else
            echo -en "\e[32m[PASS]\e[0m The Root User's Bash History is not present\n"
            ((PASS++))
        fi
    fi
    echo -en "\n\n"
    return 1
}

function checkFirewall {
    if [[ $OS == "Ubuntu" ]]; then
      fw="ufw"
      ufwa=$(ufw status | head -1 | sed -e "s/^Status:\ //")
      if [[ $ufwa == "active" ]]; then
        FW_VER="\e[32m[PASS]\e[0m Firewall service (${fw}) is active\n"
        ((PASS++))
      else
        FW_VER="\e[93m[WARN]\e[0m No firewall is configured. Ensure ${fw} is installed and configured\n"
        ((WARN++))
      fi
    fi
}

function checkCloudInit {
    if hash cloud-init 2>/dev/null; then
        CI="\e[32m[PASS]\e[0m Cloud-init is installed.\n"
        ((PASS++))
    else
        CI="\e[41m[FAIL]\e[0m No valid version of cloud-init was found.\n"
        ((FAIL++))
        STATUS=2
    fi
    return 1
}

function checkBitwardenLite {
    if [ -x /var/lib/cloud/scripts/per-instance/001_onboot ]; then
        echo -en "\e[32m[PASS]\e[0m Cloud-init first-boot script is present and executable.\n"
        ((PASS++))
    else
        echo -en "\e[41m[FAIL]\e[0m Cloud-init first-boot script not found at /var/lib/cloud/scripts/per-instance/001_onboot.\n"
        ((FAIL++))
        STATUS=2
    fi
}

function checkDockerCompose {
    if docker compose version > /dev/null 2>&1; then
        echo -en "\e[32m[PASS]\e[0m Docker Compose plugin is installed.\n"
        ((PASS++))
    else
        echo -en "\e[41m[FAIL]\e[0m Docker Compose plugin is not installed.\n"
        ((FAIL++))
        STATUS=2
    fi
}

clear
echo "DigitalOcean Marketplace Image Validation Tool (Bitwarden Lite) ${VERSION}"
echo "Executed on: ${RUNDATE}"
echo "Checking local system for Marketplace compatibility..."

getDistro

echo -en "\n\e[1mDistribution:\e[0m ${OS}\n"
echo -en "\e[1mVersion:\e[0m ${VER}\n\n"

if [[ $OS == "Ubuntu" ]] && [[ $VER == "24.04" ]]; then
    echo -en "\e[32m[PASS]\e[0m Supported OS detected: ${OS} ${VER}\n"
    ((PASS++))
else
    echo -en "\e[41m[FAIL]\e[0m ${OS} ${VER} is not the expected OS (Ubuntu 24.04)\n"
    ((FAIL++))
    STATUS=2
fi

checkCloudInit
echo -en "${CI}"

checkFirewall
echo -en "${FW_VER}"

checkDockerCompose

loadPasswords
checkLogs

echo -en "\n\nChecking the root account...\n"
checkRoot

checkAgent

checkBitwardenLite

# Summary
echo -en "\n\n---------------------------------------------------------------------------------------------------\n"

if [[ $STATUS == 0 ]]; then
    echo -en "Scan Complete.\n\e[32mAll Tests Passed!\e[0m\n"
elif [[ $STATUS == 1 ]]; then
    echo -en "Scan Complete. \n\e[93mSome non-critical tests failed.  Please review these items.\e[0m\e[0m\n"
else
    echo -en "Scan Complete. \n\e[41mOne or more tests failed.  Please review these items and re-test.\e[0m\n"
fi
echo "---------------------------------------------------------------------------------------------------"
echo -en "\e[1m${PASS} Tests PASSED\e[0m\n"
echo -en "\e[1m${WARN} WARNINGS\e[0m\n"
echo -en "\e[1m${FAIL} Tests FAILED\e[0m\n"
echo -en "---------------------------------------------------------------------------------------------------\n"

if [[ $STATUS == 0 ]]; then
    echo -en "We did not detect any issues with this image.\n\n"
    exit 0
elif [[ $STATUS == 1 ]]; then
    echo -en "Please review all [WARN] items above and ensure they are intended or resolved.\n\n"
    exit 0
else
    echo -en "Some critical tests failed.  These items must be resolved and this scan re-run before you submit your image to the DigitalOcean Marketplace.\n\n"
    exit 1
fi
