#!/usr/bin/env bash
set -e

cat << "EOF"
 _     _ _                         _            
| |__ (_) |___      ____ _ _ __ __| | ___ _ __  
| '_ \| | __\ \ /\ / / _` | '__/ _` |/ _ \ '_ \ 
| |_) | | |_ \ V  V / (_| | | | (_| |  __/ | | |
|_.__/|_|\__| \_/\_/ \__,_|_|  \__,_|\___|_| |_|

EOF

cat << EOF
Open source password management solutions
Copyright 2015-$(date +'%Y'), 8bit Solutions LLC
https://bitwarden.com, https://github.com/bitwarden

===================================================

EOF

RED='\033[0;31m'
NC='\033[0m' # No Color

if [ "$EUID" -eq 0 ]; then
    echo -e "${RED}WARNING: This script is running as the root user!"
    echo -e "If you are running a standard deployment this script should be running as a dedicated Bitwarden User as per the documentation.${NC}"
    read -p "Do you still want to continue? (y/n): " choice

    # Check the user's choice
    case "$choice" in
        [Yy]|[Yy][Ee][Ss])
            echo -e "Continuing...."
            ;;
        *)
            exit 1         
            ;;
    esac
fi



# Setup

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SCRIPT_NAME=$(basename "$0")
SCRIPT_PATH="$DIR/$SCRIPT_NAME"
OUTPUT="$DIR/bwdata"
if [ $# -eq 2 ]
then
    OUTPUT=$2
fi
if command -v docker-compose &> /dev/null
then
    dccmd='docker-compose'
else
    dccmd='docker compose'
fi

SCRIPTS_DIR="$OUTPUT/scripts"
BITWARDEN_SCRIPT_URL="https://func.bitwarden.com/api/dl/?app=self-host&platform=linux"
RUN_SCRIPT_URL="https://func.bitwarden.com/api/dl/?app=self-host&platform=linux&variant=run"

# Please do not create pull requests modifying the version numbers.
COREVERSION="2024.2.3"
WEBVERSION="2024.2.5"
KEYCONNECTORVERSION="2023.12.0"

echo "bitwarden.sh version $COREVERSION"
docker --version
if [[ "$dccmd" == "docker compose" ]]; then
    $dccmd version
else
    $dccmd --version
fi

echo ""

# Functions

function downloadSelf() {
    if curl -L -s -w "http_code %{http_code}" -o $SCRIPT_PATH.1 $BITWARDEN_SCRIPT_URL | grep -q "^http_code 20[0-9]"
    then
        mv -f $SCRIPT_PATH.1 $SCRIPT_PATH
        chmod u+x $SCRIPT_PATH
    else
        rm -f $SCRIPT_PATH.1
    fi
}

function downloadRunFile() {
    if [ ! -d "$SCRIPTS_DIR" ]
    then
        mkdir $SCRIPTS_DIR
    fi
    run_file_status_code=$(curl -s -L -w "%{http_code}" -o $SCRIPTS_DIR/run.sh $RUN_SCRIPT_URL)
    if echo "$run_file_status_code" | grep -q "^20[0-9]"
    then
        chmod u+x $SCRIPTS_DIR/run.sh
        rm -f $SCRIPTS_DIR/install.sh
    else
        echo "Unable to download run script from $RUN_SCRIPT_URL. Received status code: $run_file_status_code"
        echo "http response:"
        cat $SCRIPTS_DIR/run.sh
        rm -f $SCRIPTS_DIR/run.sh
        exit 1
    fi
}

function checkOutputDirExists() {
    if [ ! -d "$OUTPUT" ]
    then
        echo "Cannot find a Bitwarden installation at $OUTPUT."
        exit 1
    fi
}

function checkOutputDirNotExists() {
    if [ -d "$OUTPUT/docker" ]
    then
        echo "Looks like Bitwarden is already installed at $OUTPUT."
        exit 1
    fi
}

function checkSmtp() {
    CONFIG_FILE="$1/env/global.override.env"

    if [ ! -f "$CONFIG_FILE" ]; then
        echo "Configuration file not found at $CONFIG_FILE"
        exit 1
    fi

    host=$(grep 'globalSettings__mail__smtp__host=' "$CONFIG_FILE" | cut -d '=' -f2)
    port=$(grep 'globalSettings__mail__smtp__port=' "$CONFIG_FILE" | cut -d '=' -f2)
    ssl=$(grep 'globalSettings__mail__smtp__ssl=' "$CONFIG_FILE" | cut -d '=' -f2)
    username=$(grep 'globalSettings__mail__smtp__username=' "$CONFIG_FILE" | cut -d '=' -f2)
    password=$(grep 'globalSettings__mail__smtp__password=' "$CONFIG_FILE" | cut -d '=' -f2)

    if [ "$ssl" == "true" ]; then
        ssl_command="-ssl"
    else
        ssl_command="-starttls smtp"
    fi

    SMTP_RESPONSE=$(
        {
            echo "EHLO localhost"
            if [ "$ssl_command" != "-ssl" ]; then
                echo "STARTTLS"
                sleep 2
                echo "EHLO localhost"
            fi
            echo "AUTH LOGIN"
            echo "$(echo -ne "$username" | base64)"
            echo "$(echo -ne "$password" | base64)"
            echo "QUIT"
        } | openssl s_client -connect $host:$port $ssl_command -ign_eof 2>/dev/null
    )

    if echo "$SMTP_RESPONSE" | grep -q "235 "; then
        echo -e "SMTP settings are correct."
    else
        echo "SMTP authentication failed or connection error occurred."
    fi
}

function listCommands() {
cat << EOT
Available commands:

install
start
restart
stop
update
updatedb
updaterun
updateself
updateconf
uninstall
renewcert
rebuild
help

See more at https://bitwarden.com/help/article/install-on-premise/#script-commands-reference

EOT
}

# Commands

case $1 in
    "install")
        checkOutputDirNotExists
        mkdir -p $OUTPUT
        downloadRunFile
        $SCRIPTS_DIR/run.sh install $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "start" | "restart")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh restart $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "update")
        checkOutputDirExists
        downloadRunFile
        $SCRIPTS_DIR/run.sh update $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "rebuild")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh rebuild $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "updateconf")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh updateconf $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "updatedb")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh updatedb $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "stop")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh stop $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "renewcert")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh renewcert $OUTPUT $COREVERSION $WEBVERSION $KEYCONNECTORVERSION
        ;;
    "updaterun")
        checkOutputDirExists
        downloadRunFile
        ;;
    "updateself")
        downloadSelf && echo "Updated self." && exit
        ;;
    "uninstall")
        checkOutputDirExists
        $SCRIPTS_DIR/run.sh uninstall $OUTPUT
        ;;
    "checksmtp")
        checkOutputDirExists
        checkSmtp $OUTPUT
        ;;
    "help")
        listCommands
        ;;
    *)
        echo "No command found."
        echo
        listCommands
esac
