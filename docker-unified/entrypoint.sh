#!/bin/bash

if [[ "$(id -u)" == "0" ]]; then
  # Set up user group
  PGID="${PGID:-911}"
  addgroup --gid "$PGID" bitwarden

  # Set up user
  PUID="${PUID:-911}"
  adduser --no-create-home --shell /bin/bash --disabled-password --uid "$PUID" --gid "$PGID" --gecos "" bitwarden
fi

# Translate environment variables for application settings
VAULT_SERVICE_URI=https://$BW_DOMAIN
MYSQL_CONNECTION_STRING="server=$BW_DB_SERVER;port=${BW_DB_PORT:-3306};database=$BW_DB_DATABASE;user=$BW_DB_USERNAME;password=$BW_DB_PASSWORD"
POSTGRESQL_CONNECTION_STRING="Host=$BW_DB_SERVER;Port=${BW_DB_PORT:-5432};Database=$BW_DB_DATABASE;Username=$BW_DB_USERNAME;Password=$BW_DB_PASSWORD"
SQLSERVER_CONNECTION_STRING="Server=$BW_DB_SERVER,${BW_DB_PORT:-1433};Database=$BW_DB_DATABASE;User Id=$BW_DB_USERNAME;Password=$BW_DB_PASSWORD;Encrypt=True;TrustServerCertificate=True"
SQLITE_CONNECTION_STRING="Data Source=$BW_DB_FILE;"
INTERNAL_IDENTITY_KEY=$(openssl rand -hex 30)
OIDC_IDENTITY_CLIENT_KEY=$(openssl rand -hex 30)
DUO_AKEY=$(openssl rand -hex 30)

export globalSettings__baseServiceUri__vault=${globalSettings__baseServiceUri__vault:-$VAULT_SERVICE_URI}
export globalSettings__installation__id=$BW_INSTALLATION_ID
export globalSettings__installation__key=$BW_INSTALLATION_KEY
export globalSettings__internalIdentityKey=${globalSettings__internalIdentityKey:-$INTERNAL_IDENTITY_KEY}
export globalSettings__oidcIdentityClientKey=${globalSettings__oidcIdentityClientKey:-$OIDC_IDENTITY_CLIENT_KEY}
export globalSettings__duo__aKey=${globalSettings__duo__aKey:-$DUO_AKEY}

export globalSettings__databaseProvider=$BW_DB_PROVIDER
export globalSettings__mysql__connectionString=${globalSettings__mysql__connectionString:-$MYSQL_CONNECTION_STRING}
export globalSettings__postgreSql__connectionString=${globalSettings__postgreSql__connectionString:-$POSTGRESQL_CONNECTION_STRING}
export globalSettings__sqlServer__connectionString=${globalSettings__sqlServer__connectionString:-$SQLSERVER_CONNECTION_STRING}
export globalSettings__sqlite__connectionString=${globalSettings__sqlite__connectionString:-$SQLITE_CONNECTION_STRING}

if [ "$BW_ENABLE_SSL" = "true" ]; then
  export globalSettings__baseServiceUri__internalVault=https://localhost:${BW_PORT_HTTPS:-8443}
else
  export globalSettings__baseServiceUri__internalVault=http://localhost:${BW_PORT_HTTP:-8080}
fi

# Generate Identity certificate
if [ ! -f /etc/bitwarden/identity.pfx ]; then
  openssl req \
  -x509 \
  -newkey rsa:4096 \
  -sha256 \
  -nodes \
  -keyout /etc/bitwarden/identity.key \
  -out /etc/bitwarden/identity.crt \
  -subj "/CN=Bitwarden IdentityServer" \
  -days 36500
  
  # identity.pfx is soft linked to the necessary locations in the Dockerfile
  openssl pkcs12 \
  -export \
  -out /etc/bitwarden/identity.pfx \
  -inkey /etc/bitwarden/identity.key \
  -in /etc/bitwarden/identity.crt \
  -passout "pass:$globalSettings__identityServer__certificatePassword"
  
  rm /etc/bitwarden/identity.crt
  rm /etc/bitwarden/identity.key
fi

# Generate SSL certificates
if [ "$BW_ENABLE_SSL" = "true" ] && [ ! -f "/etc/bitwarden/${BW_SSL_KEY:-ssl.key}" ]; then
  openssl req \
  -x509 \
  -newkey rsa:4096 \
  -sha256 \
  -nodes \
  -days 36500 \
  -keyout /etc/bitwarden/"${BW_SSL_KEY:-ssl.key}" \
  -out /etc/bitwarden/"${BW_SSL_CERT:-ssl.crt}" \
  -reqexts SAN \
  -extensions SAN \
  -config <(cat /usr/lib/ssl/openssl.cnf <(printf '[SAN]\nsubjectAltName=DNS:%s\nbasicConstraints=CA:true' "${BW_DOMAIN:-localhost}")) \
  -subj "/C=US/ST=California/L=Santa Barbara/O=Bitwarden Inc./OU=Bitwarden/CN=${BW_DOMAIN:-localhost}"
fi

# Launch a loop to rotate nginx logs on a daily basis
/bin/sh -c "/logrotate.sh loop >/dev/null 2>&1 &"

# Create necessary directories
mkdir -p /etc/bitwarden/logs/supervisord
mkdir -p /etc/bitwarden/logs/nginx
mkdir -p /etc/bitwarden/nginx
mkdir -p /tmp/bitwarden

/usr/local/bin/hbs

if [[ "$(id -u)" == 0 ]]; then
  find /etc/bitwarden ! -xtype l \( ! -gid "$PGID" -o ! -uid "$PUID" \) -exec chown "${PUID}:${PGID}" {} +

  exec setpriv --reuid="$PUID" --regid="$PGID" --init-groups /usr/bin/supervisord
else
  FILES="$(find /etc/bitwarden ! -xtype l \( ! -gid "$(id -g)" -o ! -uid "$(id -u)" \))"
  if [[ -n "$FILES" ]]; then
    echo "The following files are not owned by the running user and may cause errors:" >&2
    echo "$FILES" >&2
  fi

  exec /usr/bin/supervisord
fi