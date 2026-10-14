#!/bin/sh

# Back up the database and /etc/bitwarden (including the data-protection key
# ring, without which the dump cannot restore). See README.md in this directory.
#
# Usage: backup-db.sh [loop]
#   loop           sleep until the next interval before each backup, and never exit
#   (no argument)  take one backup now and exit

# Checked here, not in supervisord's autostart, so a bad value can't stop the container.
BACKUP_DB=${BACKUP_DB:-1}
if [ "$BACKUP_DB" = "0" ]; then
  echo "backup: BACKUP_DB=0 — backups are disabled."
  exit 0
fi

# Same knobs and defaults as standard self-host's util/MsSql/backup-db.sh.
BACKUP_INTERVAL=${BACKUP_INTERVAL:-next day}
BACKUP_INTERVAL_FORMAT=${BACKUP_INTERVAL_FORMAT:-%Y-%m-%d 00:00:00}
BACKUP_TIMEZONE=${BACKUP_TIMEZONE:-UTC}
export TZ="$BACKUP_TIMEZONE"

BACKUP_DIR=${BACKUP_DIR:-/backups}
BACKUP_CONFIG_DIR=${BACKUP_CONFIG_DIR:-/etc/bitwarden}
BACKUP_ON_START=${BACKUP_ON_START:-0}

BACKUP_RETENTION_DAYS=${BACKUP_RETENTION_DAYS:-32}
BACKUP_FILE_MODE=${BACKUP_FILE_MODE:-600}
BACKUP_INCLUDE_SETTINGS=${BACKUP_INCLUDE_SETTINGS:-false}
BACKUP_SETTINGS_FILE=${BACKUP_SETTINGS_FILE:-/settings.env}

# CLIENT_ARGS go to every client call, backup and restore; EXTRA_ARGS to mariadb-dump only.
BACKUP_DB_CLIENT_ARGS=${BACKUP_DB_CLIENT_ARGS:-}
BACKUP_DB_EXTRA_ARGS=${BACKUP_DB_EXTRA_ARGS:-}

ARCHIVE_PREFIX=bitwarden-lite

umask 077

log() { echo "backup: $(date '+%Y-%m-%d %H:%M:%S%z') $*"; }
fail() { echo "backup: $(date '+%Y-%m-%d %H:%M:%S%z') ERROR $*" >&2; }

# BusyBox date can't parse "next day" and BusyBox tar has no --transform.
if ! date -d "next day" >/dev/null 2>&1; then
  fail "date cannot parse a relative date; the image must install coreutils."
  exit 1
fi
if ! tar --help 2>&1 | grep -q -- "--transform"; then
  fail "tar does not support --transform; the image must install GNU tar."
  exit 1
fi

# Treat an unreadable mountinfo as mounted rather than refuse on an unknown runtime.
is_mounted() {
  [ -r /proc/self/mountinfo ] || return 0
  awk -v m="$1" '$5 == m { found = 1 } END { exit !found }' /proc/self/mountinfo
}

provider() {
  case "${BW_DB_PROVIDER:-mysql}" in
    mysql|mariadb) echo mysql ;;
    sqlite) echo sqlite ;;
    *) echo unsupported ;;
  esac
}

# Echoes the dump's file name; leaves no partial file on failure.
dump_database() {
  stage_db=$1
  now=$2

  case "$(provider)" in
    mysql)
      dump_name="vault_${now}.sql"
      target="${stage_db}/${dump_name}"

      for var in BW_DB_SERVER BW_DB_DATABASE BW_DB_USERNAME BW_DB_PASSWORD; do
        eval "value=\${$var:-}"
        if [ -z "$value" ]; then
          fail "$var is not set; cannot dump the database."
          return 1
        fi
      done

      # MYSQL_PWD, not -p: command-line arguments are visible in /proc.
      MYSQL_PWD="$BW_DB_PASSWORD"
      export MYSQL_PWD

      set -- \
        --host="$BW_DB_SERVER" \
        --port="${BW_DB_PORT:-3306}" \
        --user="$BW_DB_USERNAME" \
        --single-transaction \
        --routines \
        --triggers \
        --no-tablespaces \
        --default-character-set=utf8mb4

      # shellcheck disable=SC2086
      [ -n "$BACKUP_DB_CLIENT_ARGS" ] && set -- "$@" $BACKUP_DB_CLIENT_ARGS
      # shellcheck disable=SC2086
      [ -n "$BACKUP_DB_EXTRA_ARGS" ] && set -- "$@" $BACKUP_DB_EXTRA_ARGS

      mariadb-dump "$@" "$BW_DB_DATABASE" --result-file="$target"
      status=$?
      unset MYSQL_PWD
      ;;

    sqlite)
      dump_name="vault_${now}.db"
      target="${stage_db}/${dump_name}"
      db_file=${BW_DB_FILE:-/etc/bitwarden/vault.db}

      if [ ! -r "$db_file" ]; then
        fail "BW_DB_PROVIDER=sqlite but ${db_file} is not readable."
        return 1
      fi

      # .backup takes a consistent snapshot of the live database.
      sqlite3 "$db_file" ".backup '${target}'"
      status=$?
      ;;

    *)
      fail "BW_DB_PROVIDER=${BW_DB_PROVIDER:-} is not supported."
      fail "This backup covers mysql/mariadb and sqlite. PostgreSQL and SQL"
      fail "Server under Lite are not implemented — back them up yourself, and"
      fail "include ${BACKUP_CONFIG_DIR} when you do or the dump will not restore."
      return 1
      ;;
  esac

  if [ $status -ne 0 ]; then
    fail "the dump exited $status"
    rm -f "$target"
    return $status
  fi

  if [ ! -s "$target" ]; then
    fail "dump file is empty"
    rm -f "$target"
    return 1
  fi

  echo "$dump_name"
  return 0
}

server_version() {
  if [ "$(provider)" = "sqlite" ]; then
    sqlite3 --version 2>/dev/null | cut -d' ' -f1 || echo "unknown"
    return
  fi
  MYSQL_PWD="${BW_DB_PASSWORD:-}"
  export MYSQL_PWD
  # shellcheck disable=SC2086
  mariadb --host="${BW_DB_SERVER:-}" --port="${BW_DB_PORT:-3306}" \
    --user="${BW_DB_USERNAME:-}" $BACKUP_DB_CLIENT_ARGS --batch --skip-column-names \
    --execute="SELECT VERSION()" 2>/dev/null || echo "unknown"
  unset MYSQL_PWD
}

write_manifest() {
  target=$1
  dump_name=$2
  dump_path=$3

  cat > "$target" <<MANIFEST
Bitwarden Lite backup
=====================

created:          $(date '+%Y-%m-%d %H:%M:%S%z')
deployment:       bitwarden-lite
db provider:      ${BW_DB_PROVIDER:-mysql}
db name:          ${BW_DB_DATABASE:-}
db server:        $(server_version)
lite image tag:   ${BACKUP_LITE_TAG:-unknown (TAG not set in the environment)}
config source:    ${BACKUP_CONFIG_DIR}
dump sha256:      $(sha256sum "$dump_path" | cut -d' ' -f1)

contents
--------
database/${dump_name}   database dump
config/                 ${BACKUP_CONFIG_DIR} excluding logs/ (and, for sqlite, the live database file)
$([ "$BACKUP_INCLUDE_SETTINGS" = "true" ] && echo "settings.env            host settings file")

not included
------------
config/logs/            runtime logs
$([ "$BACKUP_INCLUDE_SETTINGS" = "true" ] || echo "settings.env            host-side; see below")

restoring
---------
Both the dump and config/ are required. config/data-protection holds the
ASP.NET Core key ring that User.Key and User.MasterPassword are encrypted with;
restoring the dump against a different or missing key ring leaves every account
permanently undecryptable.
$([ "$BACKUP_INCLUDE_SETTINGS" = "true" ] || cat <<'SETTINGS'

settings.env is not in this archive. It lives on the host next to
docker-compose.yml and holds BW_INSTALLATION_ID, BW_INSTALLATION_KEY and
BW_DB_PASSWORD. Back it up separately — without it you cannot reach the
database this dump belongs to.
SETTINGS
)

See bitwarden-lite/backup/README.md for the restore procedure.

WARNING: this archive contains your full encrypted vault and, in
config/data-protection, the plaintext keys that protect it. Store it as you
would store the vault itself.
MANIFEST
}

# Written as .partial and renamed only once verified.
run_backup() {
  now=$(date +%Y%m%d_%H%M%S)
  archive="${BACKUP_DIR}/${ARCHIVE_PREFIX}_${now}.tar.gz"
  partial="${archive}.partial"

  if [ ! -d "$BACKUP_DIR" ] || [ ! -w "$BACKUP_DIR" ]; then
    fail "${BACKUP_DIR} does not exist or is not writable as $(id -u):$(id -g)."
    fail "Mount a host directory at ${BACKUP_DIR} — see bitwarden-lite/backup/README.md."
    return 1
  fi

  if ! is_mounted "$BACKUP_DIR"; then
    fail "nothing is mounted at ${BACKUP_DIR}, so an archive would be written inside the"
    fail "container and lost when it is recreated. Mount a host directory at ${BACKUP_DIR}"
    fail "(e.g. \"./backups:/backups\" in docker-compose.yml) — see bitwarden-lite/backup/README.md."
    return 1
  fi

  # Not /tmp: attachments can exceed the container's tmpfs.
  stage=$(mktemp -d "${BACKUP_DIR}/.staging_XXXXXX") \
    || { fail "could not create staging directory under ${BACKUP_DIR}"; return 1; }
  # shellcheck disable=SC2064
  trap "rm -rf '$stage'" EXIT INT TERM

  mkdir -p "$stage/database"

  log "dumping ${BW_DB_DATABASE:-${BW_DB_FILE:-}} (provider ${BW_DB_PROVIDER:-mysql})"
  dump_name=$(dump_database "$stage/database" "$now")
  if [ -z "$dump_name" ]; then
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi
  log "dump complete ($(du -h "$stage/database/$dump_name" | cut -f1))"

  if [ ! -r "$BACKUP_CONFIG_DIR" ]; then
    fail "${BACKUP_CONFIG_DIR} is not readable as $(id -u):$(id -g);"
    fail "refusing to write a dump-only archive, which cannot restore a vault."
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  # The app creates the key ring on first use; an archive without it can't restore.
  set -- "$BACKUP_CONFIG_DIR"/data-protection/key-*.xml
  if [ ! -r "$1" ]; then
    fail "no readable data-protection keys in ${BACKUP_CONFIG_DIR}/data-protection;"
    fail "the application has not created its key ring yet, or BACKUP_CONFIG_DIR is wrong."
    fail "refusing to write an archive that cannot restore a vault."
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  write_manifest "$stage/manifest.txt" "$dump_name" "$stage/database/$dump_name"

  if [ "$BACKUP_INCLUDE_SETTINGS" = "true" ]; then
    if [ -r "$BACKUP_SETTINGS_FILE" ]; then
      cp "$BACKUP_SETTINGS_FILE" "$stage/settings.env"
    else
      fail "BACKUP_INCLUDE_SETTINGS=true but ${BACKUP_SETTINGS_FILE} is not readable"
      rm -rf "$stage"; trap - EXIT INT TERM
      return 1
    fi
  fi

  # Two passes so --transform applies only to the config tree.
  tarball="$stage/archive.tar"

  set -- ./manifest.txt ./database
  [ "$BACKUP_INCLUDE_SETTINGS" = "true" ] && set -- "$@" ./settings.env

  if ! tar -cf "$tarball" -C "$stage" "$@"; then
    fail "could not archive the dump"
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  # The live SQLite files are already in database/ as a consistent snapshot.
  set --
  if [ "$(provider)" = "sqlite" ]; then
    db_file=${BW_DB_FILE:-/etc/bitwarden/vault.db}
    case "$db_file" in
      "${BACKUP_CONFIG_DIR}"/*)
        rel="./${db_file#"${BACKUP_CONFIG_DIR}"/}"
        set -- --exclude="$rel" --exclude="${rel}-wal" \
               --exclude="${rel}-shm" --exclude="${rel}-journal"
        ;;
    esac
  fi

  # ./backups too, in case it is mounted inside the config tree.
  if ! tar -rf "$tarball" -C "$BACKUP_CONFIG_DIR" \
       "$@" \
       --exclude=./logs \
       --exclude=./backups \
       --transform='s,^\./,config/,' \
       --transform='s,^\.$,config,' \
       .; then
    fail "could not archive ${BACKUP_CONFIG_DIR}"
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  if ! gzip -c "$tarball" > "$partial"; then
    fail "could not compress the archive"
    rm -f "$partial"
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  if ! tar -tzf "$partial" >/dev/null 2>&1; then
    fail "archive failed verification, discarding"
    rm -f "$partial"
    rm -rf "$stage"; trap - EXIT INT TERM
    return 1
  fi

  chmod "$BACKUP_FILE_MODE" "$partial"
  mv "$partial" "$archive"

  rm -rf "$stage"; trap - EXIT INT TERM

  log "wrote ${archive} ($(du -h "$archive" | cut -f1))"
  return 0
}

sweep_retention() {
  if [ "$BACKUP_RETENTION_DAYS" -gt 0 ] 2>/dev/null; then
    find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type f \
      -name "${ARCHIVE_PREFIX}_*.tar.gz" -mtime "+${BACKUP_RETENTION_DAYS}" \
      -print -delete | while read -r removed; do
        log "retention: removed ${removed}"
      done
  fi

  # Leftovers from interrupted runs.
  find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type f -name '*.partial' \
    -mmin +60 -print -delete | while read -r stale; do
      log "removed stale partial ${stale}"
    done

  find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d \( -name '.staging_*' -o -name '.restore_*' \) \
    -mmin +60 -print -exec rm -rf {} + | while read -r stale; do
      log "removed stale staging directory ${stale}"
    done
}

log "starting as $(id -u):$(id -g) (provider=${BW_DB_PROVIDER:-mysql} interval='${BACKUP_INTERVAL}' tz=${BACKUP_TIMEZONE} retention_days=${BACKUP_RETENTION_DAYS})"

first=1
while true
do
  if [ "${1:-}" = "loop" ]; then
    if [ "$first" = "1" ] && [ "$BACKUP_ON_START" = "1" ]; then
      log "BACKUP_ON_START=1, taking a backup now"
    else
      interval_start=$(date "+${BACKUP_INTERVAL_FORMAT} %z" -d "${BACKUP_INTERVAL}")
      log "sleeping until ${interval_start}"
      sleep $(( $(date +%_s -d "${interval_start}") - $(date +%_s) ))
    fi
  fi
  first=0

  if run_backup; then
    sweep_retention
  else
    fail "backup failed; retention sweep skipped and existing backups left alone"
    [ "${1:-}" != "loop" ] && exit 1
  fi

  [ "${1:-}" != "loop" ] && break
done
