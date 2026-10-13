#!/bin/sh

# Restore a Bitwarden Lite backup produced by backup-db.sh.
#
# The backup loop runs inside the application container, so a restore cannot
# run there — the application must be stopped, and it is the container. Instead
# run a second container from the same image with this script as the entrypoint,
# which gets the config volume read-write without the application holding it:
#
#   docker compose stop bitwarden
#   docker compose run --rm --no-deps --entrypoint /restore-db.sh bitwarden \
#     /backups/bitwarden-lite_20260915_030000.tar.gz --clean --yes
#   docker compose up -d
#
# Usage: restore-db.sh <archive.tar.gz> [options]
#   --clean         drop and recreate the database before loading the dump
#   --yes           don't prompt for confirmation
#   --db-only       restore only the database dump
#   --config-only   restore only the config tree
#
# The archive is unpacked beside itself (or under RESTORE_STAGE_DIR), not in
# /tmp, so that directory needs room for the unpacked archive.

set -eu

BACKUP_CONFIG_DIR=${BACKUP_CONFIG_DIR:-/etc/bitwarden}
# Connection options for every client call, shared with backup-db.sh — e.g.
# --skip-ssl for a server without TLS. A backup that needed them to connect
# cannot be restored without them.
BACKUP_DB_CLIENT_ARGS=${BACKUP_DB_CLIENT_ARGS:-}

# Everything unpacked here is vault data and plaintext key material.
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
    -h|--help) sed -n '3,22p' "$0"; exit 0 ;;
    -*) echo "restore: unknown option $1" >&2; exit 2 ;;
    *) archive="$1" ;;
  esac
  shift
done

log() { echo "restore: $*"; }
fail() { echo "restore: ERROR $*" >&2; exit 1; }

# The password travels in MYSQL_PWD, exported by the caller, never on the
# command line.
db_client() {
  # shellcheck disable=SC2086
  mariadb --host="$BW_DB_SERVER" --port="${BW_DB_PORT:-3306}" \
    --user="$BW_DB_USERNAME" $BACKUP_DB_CLIENT_ARGS "$@"
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

# Not /tmp: the archive unpacks to the dump plus every attachment, which can be
# far larger than the container's /tmp tmpfs (backup-db.sh stages under the
# backups directory for the same reason).
stage_parent=${RESTORE_STAGE_DIR:-$(dirname "$archive")}
stage=$(mktemp -d "${stage_parent}/.restore_XXXXXX") \
  || fail "cannot create a staging directory in ${stage_parent}; set RESTORE_STAGE_DIR to a writable directory with room for the unpacked archive"
# shellcheck disable=SC2064
trap "rm -rf '$stage'" EXIT INT TERM

log "unpacking"
tar -xzf "$archive" -C "$stage"

# Reach the database before overwriting anything, so a server that is down or
# misconfigured fails the restore while the target is still untouched. On a
# clean host the database container has usually only just been started, and a
# fresh MariaDB runs a socket-only init server for its first seconds, so wait.
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
  esac
fi

# Config before the database. For SQLite the database file lives inside the
# config tree, and archives made before the live file was excluded also carry
# a raw copy of it in config/. Restoring the snapshot last means it always wins.
if [ "$do_config" = "1" ]; then
  [ -d "$stage/config" ] || fail "no config/ tree found in the archive"
  [ -d "$BACKUP_CONFIG_DIR" ] || fail "${BACKUP_CONFIG_DIR} is not mounted"
  [ -w "$BACKUP_CONFIG_DIR" ] || fail "${BACKUP_CONFIG_DIR} is not writable"

  log "restoring config into ${BACKUP_CONFIG_DIR}"
  # Copy over the top rather than wiping first: files present on the target but
  # absent from the archive (extra data-protection keys, stray attachments) are
  # harmless, whereas deleting them is not reversible.
  cp -a "$stage/config/." "$BACKUP_CONFIG_DIR/"

  # The application expects the tree to be owned by its PUID:PGID. Its own
  # entrypoint fixes this on start when running as root, but a rootless or
  # read-only deployment cannot, so set it here while we still can.
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

    sqlite)
      dump=$(find "$stage/database" -maxdepth 1 -type f -name '*.db' 2>/dev/null | head -1)
      [ -n "$dump" ] || fail "no .db snapshot found in the archive"
      db_file=${BW_DB_FILE:-/etc/bitwarden/vault.db}

      log "restoring $(basename "$dump") to ${db_file}"
      # Remove the write-ahead log and shared-memory sidecars if present: they
      # belong to the database being replaced, and leaving them would let SQLite
      # replay them over the restored file.
      rm -f "${db_file}-wal" "${db_file}-shm"
      cp "$dump" "$db_file"
      # Ownership is fixed up for the config tree above, which ran first.
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
