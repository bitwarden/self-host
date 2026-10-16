#!/bin/sh

# Restore a backup-db.sh archive. Run it in a one-off container with the app
# stopped; see README.md in this directory.
#
# Usage: restore-db.sh <archive.tar.gz> [options]
#   --clean         replace the existing database contents
#   --yes           don't prompt for confirmation
#   --db-only       restore only the database dump
#   --config-only   restore only the config tree

set -eu

BACKUP_CONFIG_DIR=${BACKUP_CONFIG_DIR:-/etc/bitwarden}
BACKUP_DB_CLIENT_ARGS=${BACKUP_DB_CLIENT_ARGS:-}
BACKUP_MSSQL_SERVER_DIR=${BACKUP_MSSQL_SERVER_DIR:-/etc/bitwarden/mssql/backups}
BACKUP_MSSQL_SHARED_DIR=${BACKUP_MSSQL_SHARED_DIR:-/mssql-backups}
SQLCMD=/opt/mssql-tools18/bin/sqlcmd

umask 077

archive=""
clean=0
assume_yes=0
do_db=1
do_config=1

while [ $# -gt 0 ]; do
  case "$1" in
    --clean) clean=1 ;;
    --yes|-y) assume_yes=1 ;;
    --db-only) do_config=0 ;;
    --config-only) do_db=0 ;;
    -h|--help) sed -n '3,10p' "$0"; exit 0 ;;
    -*) echo "restore: unknown option $1" >&2; exit 2 ;;
    *) archive="$1" ;;
  esac
  shift
done

log() { echo "restore: $*"; }
fail() { echo "restore: ERROR $*" >&2; exit 1; }

# The caller exports MYSQL_PWD, PGPASSWORD or SQLCMDPASSWORD.
db_client() {
  # shellcheck disable=SC2086
  mariadb --host="$BW_DB_SERVER" --port="${BW_DB_PORT:-3306}" \
    --user="$BW_DB_USERNAME" $BACKUP_DB_CLIENT_ARGS "$@"
}
pg_client() {
  # shellcheck disable=SC2086
  psql --host="$BW_DB_SERVER" --port="${BW_DB_PORT:-5432}" --username="$BW_DB_USERNAME" \
    --no-password --dbname="$BW_DB_DATABASE" $BACKUP_DB_CLIENT_ARGS "$@"
}
mssql_client() {
  # shellcheck disable=SC2086
  "$SQLCMD" -S "${BW_DB_SERVER},${BW_DB_PORT:-1433}" -U "$BW_DB_USERNAME" -C -b \
    $BACKUP_DB_CLIENT_ARGS "$@"
}

[ -n "$archive" ] || fail "no archive given. Usage: restore-db.sh <archive.tar.gz> [--clean] [--yes]"
[ -r "$archive" ] || fail "cannot read ${archive}"

if [ "$do_db" = "0" ] && [ "$do_config" = "0" ]; then
  fail "--db-only and --config-only are mutually exclusive"
fi

tar -tzf "$archive" >/dev/null 2>&1 || fail "${archive} is not a readable gzip archive"

log "archive: ${archive}"
tar -xzOf "$archive" ./manifest.txt 2>/dev/null || log "note: no manifest.txt in this archive"

if [ "$do_config" = "1" ] && [ "$do_db" = "1" ]; then
  scope="the database and ${BACKUP_CONFIG_DIR}"
elif [ "$do_db" = "1" ]; then
  scope="the database only"
else
  scope="${BACKUP_CONFIG_DIR} only"
fi

if [ "$do_db" = "1" ] && [ "$do_config" = "0" ]; then
  log ""
  log "WARNING: a database-only restore does not produce a working instance."
  log "User.Key and User.MasterPassword are encrypted with the key ring in"
  log "${BACKUP_CONFIG_DIR}/data-protection. Without the matching ring, no"
  log "account can log in and no vault can be decrypted."
  log ""
fi

if [ "$assume_yes" != "1" ]; then
  printf 'restore: this will overwrite %s. Continue? [y/N] ' "$scope"
  read -r answer
  case "$answer" in
    y|Y|yes|YES) ;;
    *) log "aborted."; exit 0 ;;
  esac
fi

# Not /tmp: attachments can exceed the container's tmpfs.
stage_parent=${RESTORE_STAGE_DIR:-$(dirname "$archive")}
stage=$(mktemp -d "${stage_parent}/.restore_XXXXXX") \
  || fail "cannot create a staging directory in ${stage_parent}; set RESTORE_STAGE_DIR to a writable directory with room for the unpacked archive"
# shellcheck disable=SC2064
trap "rm -rf '$stage'" EXIT INT TERM

log "unpacking"
tar -xzf "$archive" -C "$stage"

# Reach the database before changing anything; a freshly started one needs a moment.
if [ "$do_db" = "1" ]; then
  case "${BW_DB_PROVIDER:-mysql}" in
    mysql|mariadb)
      for var in BW_DB_SERVER BW_DB_DATABASE BW_DB_USERNAME BW_DB_PASSWORD; do
        eval "value=\${$var:-}"
        [ -n "$value" ] || fail "$var is not set; cannot reach the database"
      done
      MYSQL_PWD="$BW_DB_PASSWORD"
      export MYSQL_PWD
      log "waiting for the database server at ${BW_DB_SERVER}:${BW_DB_PORT:-3306}"
      tries=0
      until err=$(db_client --execute="SELECT 1" 2>&1 >/dev/null); do
        tries=$((tries + 1))
        if [ "$tries" -ge 60 ]; then
          fail "cannot connect to ${BW_DB_SERVER}:${BW_DB_PORT:-3306} as ${BW_DB_USERNAME} after 120s; nothing has been changed. Last error: ${err}"
        fi
        sleep 2
      done
      ;;
    postgresql|postgres)
      for var in BW_DB_SERVER BW_DB_DATABASE BW_DB_USERNAME BW_DB_PASSWORD; do
        eval "value=\${$var:-}"
        [ -n "$value" ] || fail "$var is not set; cannot reach the database"
      done
      PGPASSWORD="$BW_DB_PASSWORD"
      export PGPASSWORD
      log "waiting for the database server at ${BW_DB_SERVER}:${BW_DB_PORT:-5432}"
      tries=0
      until err=$(pg_client --command="SELECT 1" 2>&1 >/dev/null); do
        tries=$((tries + 1))
        if [ "$tries" -ge 60 ]; then
          fail "cannot connect to ${BW_DB_SERVER}:${BW_DB_PORT:-5432} as ${BW_DB_USERNAME} after 120s; nothing has been changed. Last error: ${err}"
        fi
        sleep 2
      done
      ;;
    sqlserver|mssql)
      [ -x "$SQLCMD" ] || fail "this image has no SQL Server tools (amd64 only: SQL Server has no ARM build); nothing has been changed"
      for var in BW_DB_SERVER BW_DB_DATABASE BW_DB_USERNAME BW_DB_PASSWORD; do
        eval "value=\${$var:-}"
        [ -n "$value" ] || fail "$var is not set; cannot reach the database"
      done
      [ -d "$BACKUP_MSSQL_SHARED_DIR" ] && [ -w "$BACKUP_MSSQL_SHARED_DIR" ] \
        || fail "${BACKUP_MSSQL_SHARED_DIR} is not mounted or not writable, so the backup cannot be handed to SQL Server; nothing has been changed. See bitwarden-lite/backup/README.md"
      SQLCMDPASSWORD="$BW_DB_PASSWORD"
      export SQLCMDPASSWORD
      log "waiting for the database server at ${BW_DB_SERVER}:${BW_DB_PORT:-1433}"
      # SQL Server takes longer than the others to accept connections on first start.
      tries=0
      until err=$(mssql_client -Q "SELECT 1" 2>&1 >/dev/null); do
        tries=$((tries + 1))
        if [ "$tries" -ge 90 ]; then
          fail "cannot connect to ${BW_DB_SERVER}:${BW_DB_PORT:-1433} as ${BW_DB_USERNAME} after 180s; nothing has been changed. Last error: ${err}"
        fi
        sleep 2
      done
      ;;
  esac
fi

# Config before the database, so a SQLite snapshot always wins over any copy in config/.
if [ "$do_config" = "1" ]; then
  [ -d "$stage/config" ] || fail "no config/ tree found in the archive"
  [ -d "$BACKUP_CONFIG_DIR" ] || fail "${BACKUP_CONFIG_DIR} is not mounted"
  [ -w "$BACKUP_CONFIG_DIR" ] || fail "${BACKUP_CONFIG_DIR} is not writable"

  log "restoring config into ${BACKUP_CONFIG_DIR}"
  # Copy over the top: deleting extra files on the target is not reversible.
  cp -a "$stage/config/." "$BACKUP_CONFIG_DIR/"

  # Rootless deployments can't fix ownership at start, so do it here.
  if [ "$(id -u)" = "0" ]; then
    chown -R "${PUID:-911}:${PGID:-911}" "$BACKUP_CONFIG_DIR"
    log "config ownership set to ${PUID:-911}:${PGID:-911}"
  fi

  log "config restored"
fi

if [ "$do_db" = "1" ]; then
  case "${BW_DB_PROVIDER:-mysql}" in
    mysql|mariadb)
      dump=$(find "$stage/database" -maxdepth 1 -type f -name '*.sql' 2>/dev/null | head -1)
      [ -n "$dump" ] || fail "no .sql dump found in the archive"

      for var in BW_DB_SERVER BW_DB_DATABASE BW_DB_USERNAME BW_DB_PASSWORD; do
        eval "value=\${$var:-}"
        [ -n "$value" ] || fail "$var is not set; cannot reach the database"
      done

      MYSQL_PWD="$BW_DB_PASSWORD"
      export MYSQL_PWD

      if [ "$clean" = "1" ]; then
        log "dropping and recreating ${BW_DB_DATABASE}"
        db_client \
          --execute="DROP DATABASE IF EXISTS \`${BW_DB_DATABASE}\`; CREATE DATABASE \`${BW_DB_DATABASE}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;" \
          || fail "could not recreate the database"
      fi

      log "loading $(basename "$dump") into ${BW_DB_DATABASE}"
      db_client "$BW_DB_DATABASE" < "$dump" || fail "loading the dump failed"

      unset MYSQL_PWD
      ;;

    postgresql|postgres)
      dump=$(find "$stage/database" -maxdepth 1 -type f -name '*.dump' 2>/dev/null | head -1)
      [ -n "$dump" ] || fail "no .dump file found in the archive"

      set -- --no-owner --no-acl
      [ "$clean" = "1" ] && set -- "$@" --clean --if-exists
      pg_restore "$@" --file="$stage/restore.sql" "$dump" || fail "could not read $(basename "$dump")"

      # pg_dump 17+ emits a setting that older servers reject.
      server_num=$(pg_client --tuples-only --no-align --command="SHOW server_version_num") \
        || fail "could not query the server version"
      if [ "$server_num" -lt 170000 ]; then
        sed -i '/^SET transaction_timeout = /d' "$stage/restore.sql"
      fi

      log "loading $(basename "$dump") into ${BW_DB_DATABASE} in a single transaction"
      pg_client --quiet --set=ON_ERROR_STOP=1 --single-transaction \
        --file="$stage/restore.sql" >/dev/null || fail "loading the dump failed; nothing was changed"

      unset PGPASSWORD
      ;;

    sqlserver|mssql)
      dump=$(find "$stage/database" -maxdepth 1 -type f -name '*.bak' 2>/dev/null | head -1)
      [ -n "$dump" ] || fail "no .bak file found in the archive"
      name=$(basename "$dump")

      # SQL Server reads the file from its side of the shared volume, as its own user.
      cp "$dump" "${BACKUP_MSSQL_SHARED_DIR}/${name}" || fail "could not copy ${name} to ${BACKUP_MSSQL_SHARED_DIR}"
      if [ "$(id -u)" = "0" ]; then
        chown "${PUID:-911}:${PGID:-911}" "${BACKUP_MSSQL_SHARED_DIR}/${name}"
      fi

      db_ident=$(printf '%s' "$BW_DB_DATABASE" | sed 's/]/]]/g')
      with="CHECKSUM"
      [ "$clean" = "1" ] && with="REPLACE, CHECKSUM"

      log "restoring ${name} into ${BW_DB_DATABASE}"
      if ! out=$(mssql_client -d master \
          -Q "RESTORE DATABASE [${db_ident}] FROM DISK = N'${BACKUP_MSSQL_SERVER_DIR}/${name}' WITH ${with}" 2>&1); then
        rm -f "${BACKUP_MSSQL_SHARED_DIR}/${name}"
        echo "$out" | tail -5 >&2
        if [ "$clean" != "1" ] && echo "$out" | grep -q "Msg 3154\|already exists"; then
          fail "the database already exists; rerun with --clean to replace it"
        fi
        fail "RESTORE DATABASE failed"
      fi
      rm -f "${BACKUP_MSSQL_SHARED_DIR}/${name}"

      unset SQLCMDPASSWORD
      ;;

    sqlite)
      dump=$(find "$stage/database" -maxdepth 1 -type f -name '*.db' 2>/dev/null | head -1)
      [ -n "$dump" ] || fail "no .db snapshot found in the archive"
      db_file=${BW_DB_FILE:-/etc/bitwarden/vault.db}

      log "restoring $(basename "$dump") to ${db_file}"
      # Stale -wal/-shm files would be replayed over the restored database.
      rm -f "${db_file}-wal" "${db_file}-shm"
      cp "$dump" "$db_file"
      if [ "$(id -u)" = "0" ]; then
        chown "${PUID:-911}:${PGID:-911}" "$db_file"
      fi
      ;;

    *)
      fail "BW_DB_PROVIDER=${BW_DB_PROVIDER:-} is not supported by this restore"
      ;;
  esac
  log "database restored"
fi

log "done. Start the application, then verify an existing user can log in and decrypt their vault."
