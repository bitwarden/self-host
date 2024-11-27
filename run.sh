#!/usr/bin/env bash
set -e

# Setup
if docker compose &> /dev/null; then
    dccmd='docker compose'
elif command -v docker-compose &> /dev/null; then
    dccmd='docker-compose'
    echo "docker compose not found, falling back to docker-compose."
else
    echo "Error: Neither 'docker compose' nor 'docker-compose' commands were found. Please install Docker Compose." >&2
    exit 1
fi

CYAN='\033[0;36m'
RED='\033[1;31m'
NC='\033[0m' # No Color

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

OUTPUT_DIR=".."
if [ $# -gt 1 ]
then
    OUTPUT_DIR=$2
fi

COREVERSION="latest"
if [ $# -gt 2 ]
then
    COREVERSION=$3
fi

WEBVERSION="latest"
if [ $# -gt 3 ]
then
    WEBVERSION=$4
fi

KEYCONNECTORVERSION="latest"
if [ $# -gt 4 ]
then
    KEYCONNECTORVERSION=$5
fi

OS="lin"
[ "$(uname)" == "Darwin" ] && OS="mac"
ENV_DIR="$OUTPUT_DIR/env"
DOCKER_DIR="$OUTPUT_DIR/docker"

# Initialize UID/GID which will be used to run services from within containers
if ! grep -q "^LOCAL_UID=" $ENV_DIR/uid.env 2>/dev/null || ! grep -q "^LOCAL_GID=" $ENV_DIR/uid.env 2>/dev/null
then
    LUID="LOCAL_UID=`id -u $USER`"
    [ "$LUID" == "LOCAL_UID=0" ] && LUID="LOCAL_UID=65534"
    LGID="LOCAL_GID=`id -g $USER`"
    [ "$LGID" == "LOCAL_GID=0" ] && LGID="LOCAL_GID=65534"
    mkdir -p $ENV_DIR
    echo $LUID >$ENV_DIR/uid.env
    echo $LGID >>$ENV_DIR/uid.env
fi

# Functions

function install() {
    LETS_ENCRYPT="n"
    echo -e -n "${CYAN}(!)${NC} Enter the domain name for your Bitwarden instance (ex. bitwarden.example.com): "
    read DOMAIN
    echo ""

    if [ "$DOMAIN" == "" ]
    then
        DOMAIN="localhost"
    fi

    if [ "$DOMAIN" != "localhost" ]
    then
        echo -e -n "${CYAN}(!)${NC} Do you want to use Let's Encrypt to generate a free SSL certificate? (y/n): "
        read LETS_ENCRYPT
        echo ""

        if [ "$LETS_ENCRYPT" == "y" ]
        then
            echo -e -n "${CYAN}(!)${NC} Enter your email address (Let's Encrypt will send you certificate expiration reminders): "
            read EMAIL
            echo ""

            mkdir -p $OUTPUT_DIR/letsencrypt
            docker pull certbot/certbot
            docker run -it --rm --name certbot -p 80:80 -v $OUTPUT_DIR/letsencrypt:/etc/letsencrypt/ certbot/certbot \
                certonly --standalone --noninteractive  --agree-tos --preferred-challenges http \
                --email $EMAIL -d $DOMAIN --logs-dir /etc/letsencrypt/logs
        fi
    fi

    echo -e -n "${CYAN}(!)${NC} Enter the database name for your Bitwarden instance (ex. vault): "
    read DATABASE
    echo ""

    if [ "$DATABASE" == "" ]
    then
        DATABASE="vault"
    fi

    pullSetup
    docker run -it --rm --name setup -v $OUTPUT_DIR:/bitwarden \
        --env-file $ENV_DIR/uid.env bitwarden/setup:$COREVERSION \
        dotnet Setup.dll -install 1 -domain $DOMAIN -letsencrypt $LETS_ENCRYPT -os $OS \
        -corev $COREVERSION -webv $WEBVERSION -dbname "$DATABASE" -keyconnectorv $KEYCONNECTORVERSION
}

function dockerComposeUp() {
    dockerComposeFiles
    dockerComposeVolumes
    $dccmd up -d
}

function dockerComposeDown() {
    dockerComposeFiles
    if [ $($dccmd ps | wc -l) -gt 2 ]; then
        $dccmd down
    fi
}

function dockerComposePull() {
    dockerComposeFiles
    $dccmd pull
}

function dockerComposeFiles() {
    if [ -f "${DOCKER_DIR}/docker-compose.override.yml" ]
    then
        export COMPOSE_FILE="$DOCKER_DIR/docker-compose.yml:$DOCKER_DIR/docker-compose.override.yml"
    else
        export COMPOSE_FILE="$DOCKER_DIR/docker-compose.yml"
    fi
    export COMPOSE_HTTP_TIMEOUT="300"
}

function dockerComposeVolumes() {
    createDir "core"
    createDir "core/attachments"
    createDir "logs"
    createDir "logs/admin"
    createDir "logs/api"
    createDir "logs/events"
    createDir "logs/icons"
    createDir "logs/identity"
    createDir "logs/mssql"
    createDir "logs/nginx"
    createDir "logs/notifications"
    createDir "logs/sso"
    createDir "logs/portal"
    createDir "mssql/backups"
    createDir "mssql/data"
}

function createDir() {
    if [ ! -d "${OUTPUT_DIR}/$1" ]
    then
        echo "Creating directory $OUTPUT_DIR/$1"
        mkdir -p $OUTPUT_DIR/$1
    fi
}

function dockerPrune() {
    docker image prune --all --force --filter="label=com.bitwarden.product=bitwarden" \
        --filter="label!=com.bitwarden.project=setup"
}

function updateLetsEncrypt() {
    if [ -d "${OUTPUT_DIR}/letsencrypt/live" ]
    then
        docker pull certbot/certbot
        docker run -i --rm --name certbot -p 443:443 -p 80:80 \
            -v $OUTPUT_DIR/letsencrypt:/etc/letsencrypt/ certbot/certbot \
            renew --logs-dir /etc/letsencrypt/logs
    fi
}

function forceUpdateLetsEncrypt() {
    if [ -d "${OUTPUT_DIR}/letsencrypt/live" ]
    then
        docker pull certbot/certbot
        docker run -i --rm --name certbot -p 443:443 -p 80:80 \
            -v $OUTPUT_DIR/letsencrypt:/etc/letsencrypt/ certbot/certbot \
            renew --logs-dir /etc/letsencrypt/logs --force-renew
    fi
}

function updateDatabase() {
    pullSetup
    dockerComposeFiles

    # only use container network driver if using the included mssql image
    if grep -q 'Data Source=tcp:mssql,1433' "$ENV_DIR/global.override.env"
    then
        MSSQL_ID=$($dccmd ps -q mssql)
        local docker_network_args="--network container:$MSSQL_ID"
    fi

    docker run -i --rm --name setup $docker_network_args \
        -v $OUTPUT_DIR:/bitwarden --env-file $ENV_DIR/uid.env bitwarden/setup:$COREVERSION \
        dotnet Setup.dll -update 1 -db 1 -os $OS -corev $COREVERSION -webv $WEBVERSION -keyconnectorv $KEYCONNECTORVERSION
    echo "Database update complete"
}

function updatebw() {
    KEY_CONNECTOR_ENABLED=$(grep 'enable_key_connector:' $OUTPUT_DIR/config.yml | awk '{ print $2}')
    CORE_ID=$($dccmd ps -q admin)
    WEB_ID=$($dccmd ps -q web)
    if [ "$KEY_CONNECTOR_ENABLED" = true ];
    then
        KEYCONNECTOR_ID=$($dccmd ps -q key-connector)
    fi

    if [ $KEYCONNECTOR_ID ] &&
        docker inspect --format='{{.Config.Image}}:' $CORE_ID | grep -F ":$COREVERSION:" | grep -q ":[0-9.]*:$" &&
        docker inspect --format='{{.Config.Image}}:' $WEB_ID | grep -F ":$WEBVERSION:" | grep -q ":[0-9.]*:$" &&
        docker inspect --format='{{.Config.Image}}:' $KEYCONNECTOR_ID | grep -F ":$KEYCONNECTORVERSION:" | grep -q ":[0-9.]*:$"
    then
        echo "Update not needed"
        exit
    elif
        docker inspect --format='{{.Config.Image}}:' $CORE_ID | grep -F ":$COREVERSION:" | grep -q ":[0-9.]*:$" &&
        docker inspect --format='{{.Config.Image}}:' $WEB_ID | grep -F ":$WEBVERSION:" | grep -q ":[0-9.]*:$"
    then
        echo "Update not needed"
        exit
    fi
    dockerComposeDown
    update withpull
    restart
    dockerPrune
    echo "Pausing 60 seconds for database to come online. Please wait..."
    sleep 60
}

function update() {
    if [ "$1" == "withpull" ]
    then
        pullSetup
    fi
    docker run -i --rm --name setup -v $OUTPUT_DIR:/bitwarden \
        --env-file $ENV_DIR/uid.env bitwarden/setup:$COREVERSION \
        dotnet Setup.dll -update 1 -os $OS -corev $COREVERSION -webv $WEBVERSION -keyconnectorv $KEYCONNECTORVERSION
}

function uninstall() {
    echo -e -n "${RED}(WARNING: UNINSTALL STARTED) Would you like to save the database files? (y/n): ${NC}"
    read KEEP_DATABASE

    if [ "$KEEP_DATABASE" == "y" ]
    then
        echo "Saving database files."
        tar -cvzf "./bitwarden_database.tar.gz" "$OUTPUT_DIR/mssql"
        echo -e -n "${RED}(SAVED DATABASE FILES: YES): WARNING: ALL DATA WILL BE REMOVED, INCLUDING THE FOLDER $OUTPUT_DIR): Are you sure you want to uninstall Bitwarden? (y/n): ${NC}"
        read UNINSTALL_ACTION
    else
        echo -e -n "${RED}WARNING: ALL DATA WILL BE REMOVED, INCLUDING THE FOLDER $OUTPUT_DIR): Are you sure you want to uninstall Bitwarden? (y/n): ${NC}"
        read UNINSTALL_ACTION
    fi

    if [ "$UNINSTALL_ACTION" == "y" ]
    then
        echo "Uninstalling Bitwarden..."
        dockerComposeDown
        echo "Removing $OUTPUT_DIR"
        rm -R $OUTPUT_DIR
        echo "Removing MSSQL docker volume."
        docker volume prune --force --filter="label=com.bitwarden.product=bitwarden"
        echo "Bitwarden uninstall complete!"
    else
        echo -e -n "${CYAN}(!) Bitwarden uninstall canceled. ${NC}"
        exit 1
    fi

    echo -e -n "${RED}(!) Would you like to purge all local Bitwarden container images? (y/n): ${NC}"
    read PURGE_ACTION
    if [ "$PURGE_ACTION" == "y" ]
    then
        dockerPrune
        echo -e -n "${CYAN}Bitwarden uninstall complete! ${NC}"
    fi
}

function printEnvironment() {
    pullSetup
    docker run -i --rm --name setup -v $OUTPUT_DIR:/bitwarden \
        --env-file $ENV_DIR/uid.env bitwarden/setup:$COREVERSION \
        dotnet Setup.dll -printenv 1 -os $OS -corev $COREVERSION -webv $WEBVERSION -keyconnectorv $KEYCONNECTORVERSION
}

function restart() {
    dockerComposeDown
    dockerComposePull
    updateLetsEncrypt
    dockerComposeUp
    printEnvironment
}

function certRestart() {
    dockerComposeDown
    dockerComposePull
    forceUpdateLetsEncrypt
    dockerComposeUp
    printEnvironment
}

function pullSetup() {
    docker pull bitwarden/setup:$COREVERSION
}

# Commands

case $1 in
    "install")
        install
        ;;
    "start" | "restart")
        restart
        ;;
    "pull")
        dockerComposePull
        ;;
    "stop")
        dockerComposeDown
        ;;
    "renewcert")
        certRestart
        ;;
    "updateconf")
        dockerComposeDown
        update withpull
        ;;
    "updatedb")
        updateDatabase
        ;;
    "update")
        dockerComposeFiles
        updatebw
        updateDatabase
        ;;
    "uninstall")
        dockerComposeFiles
        uninstall
        ;;
    "rebuild")
        dockerComposeDown
        update nopull
        ;;
esac
