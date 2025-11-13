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
Copyright 2015-$(date +'%Y'), Bitwarden, Inc.
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
if docker compose &> /dev/null; then
    dccmd='docker compose'
elif command -v docker-compose &> /dev/null; then
    dccmd='docker-compose'
    echo "docker compose not found, falling back to docker-compose."
else
    echo "Error: Neither 'docker compose' nor 'docker-compose' commands were found. Please install Docker Compose." >&2
    exit 1
fi

SCRIPTS_DIR="$OUTPUT/scripts"
BITWARDEN_SCRIPT_URL="https://func.bitwarden.com/api/dl/?app=self-host&platform=linux"
RUN_SCRIPT_URL="https://func.bitwarden.com/api/dl/?app=self-host&platform=linux&variant=run"

# Please do not create pull requests modifying the version numbers.
COREVERSION="2025.11.0"
WEBVERSION="2025.11.1"
KEYCONNECTORVERSION="2024.8.0"

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
    if curl -L -s -S -w "http_code %{http_code}" -o $SCRIPT_PATH.1 $BITWARDEN_SCRIPT_URL | grep -q "^http_code 20[0-9]"
    then
        mv -f $SCRIPT_PATH.1 $SCRIPT_PATH
        chmod u+x $SCRIPT_PATH
    else
        exit_code=$?
        rm -f $SCRIPT_PATH.1
        exit $exit_code
    fi
}

function downloadRunFile() {
    if [ ! -d "$SCRIPTS_DIR" ]
    then
        mkdir $SCRIPTS_DIR
    fi

    local tmp_script=$(mktemp)

    run_file_status_code=$(curl -s -S -L -w "%{http_code}" -o $tmp_script $RUN_SCRIPT_URL)
    if echo "$run_file_status_code" | grep -q "^20[0-9]"
    then
        mv $tmp_script $SCRIPTS_DIR/run.sh
        chmod u+x $SCRIPTS_DIR/run.sh
        rm -f $SCRIPTS_DIR/install.sh
    else
        echo "Unable to download run script from $RUN_SCRIPT_URL. Received status code: $run_file_status_code"
        echo "http response:"
        cat $tmp_script
        rm -f $tmp_script
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

function compressLogs() {
    LOG_DIR=${1#$(pwd)/}/logs
    START_DATE=$2
    END_DATE=$3
    tempfile=$(mktemp)

    function validateDateFormat() {
        if ! [[ $1 =~ ^[0-9]{8}$ ]]; then
            echo "Error: $2 date format is invalid. Please use YYYYMMDD."
            exit 1
        fi
    }

    function validateDateOrder() {
        if [[ $(date -d "$1" +%s) > $(date -d "$2" +%s) ]]; then
            echo "Error: start date ($1) must be earlier than end date ($2)."
            exit 1
        fi
    }

    # Validate start date format
    if [ -n "$START_DATE" ]; then
        validateDateFormat "$START_DATE" "start"
        if [ -z "$END_DATE" ]; then
            echo "Error: an end date is required when an start date is provided."
            exit 1
        fi
    fi

    # Validate end date format and order
    if [ -n "$END_DATE" ]; then
        validateDateFormat "$END_DATE" "end"
        validateDateOrder "$START_DATE" "$END_DATE"
    fi

    if [ -n "$START_DATE" ] && [ -n "$END_DATE" ]; then

        OUTPUT_FILE="bitwarden-logs-${START_DATE}-to-${END_DATE}.tar.gz"

        if [[ "$START_DATE" == "$END_DATE" ]]; then
            OUTPUT_FILE="bitwarden-logs-${START_DATE}.tar.gz"
        fi

        for d in $(seq $(date -d "$START_DATE" "+%Y%m%d") $(date -d "$END_DATE" "+%Y%m%d")); do
            # Find and list files matching the date in the filename and modification time, append to tempfile
            find $LOG_DIR \( -type f -name "*$d*.txt" -o -name "*.log" -newermt "$START_DATE" ! -newermt "$END_DATE" \) -exec bash -c 'echo "${1#./}" >> "$2"' _ {} "$tempfile" \;
        done

        echo "Compressing logs from $START_DATE to $END_DATE ..."
    else
        OUTPUT_FILE="bitwarden-logs-all.tar.gz"
        find $LOG_DIR -type f -exec bash -c 'echo "${1#./}" >> "$2"' bash {} "$tempfile" \;
        echo "Compressing all logs..."
    fi

    tar -czvf "$OUTPUT_FILE" -T "$tempfile"
    echo "Logs compressed into $(pwd $OUTPUT_FILE)/$OUTPUT_FILE"
    rm $tempfile
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
compresslogs
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
    "compresslogs")
        checkOutputDirExists
        compressLogs $OUTPUT $2 $3
        ;;
    "help")
        listCommands
        ;;
    *)
        echo "No command found."
        echo
        listCommands
esac
