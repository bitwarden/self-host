# Bitwarden Lite backups

Bitwarden Lite takes an automatic backup once a day, inside the application
container, in the same way the multi-container product does inside its database
container. Nothing extra needs to be started or scheduled.

## What is in a backup

One gzipped tar per interval, in `/backups`:

```
bitwarden-lite_20260915_030000.tar.gz
├── database/vault_20260915_030000.sql   the database dump (mysql/mariadb)
│   └──        vault_20260915_030000.dump or the dump (postgresql)
│   └──        vault_20260915_030000.db  or the database file (sqlite)
├── config/                              /etc/bitwarden, excluding logs/ (and the
│                                        live database file, for sqlite)
└── manifest.txt                         what this archive is, and its dump checksum
```

**`config/` is not optional, and a database dump on its own is not a backup.**
Bitwarden encrypts `User.Key` and `User.MasterPassword` at rest using the
ASP.NET Core data-protection key ring stored in
`/etc/bitwarden/data-protection`. Restore a dump against a missing or
regenerated key ring and no account can log in and no vault can be decrypted,
permanently — so the backup captures both together, and refuses to write an
archive at all if it cannot read the config tree.

**`settings.env` is not included by default.** It lives on the host next to
`docker-compose.yml` and holds `BW_INSTALLATION_ID`, `BW_INSTALLATION_KEY` and
`BW_DB_PASSWORD` — without it you cannot reach the database the dump belongs
to. Back it up separately, or set `BACKUP_INCLUDE_SETTINGS=true` and mount it
into the container at `/settings.env` to have it travel in every archive.

> **The archive is as sensitive as the vault itself.** It contains your full
> encrypted vault and, in `config/data-protection`, the plaintext keys that
> protect it. Store it accordingly.

## Where backups go

`/backups` in the container, which `docker-compose.yml` maps to `./backups` on
the host. Override with `BW_BACKUP_PATH`.

This is a host path rather than a named Docker volume on purpose: `docker
compose down -v` destroys named volumes, and a backup that dies with the
deployment is not a backup. Point `BW_BACKUP_PATH` at somewhere that gets
copied off the machine.

**Something must be mounted at `/backups`.** If nothing is, for example after
upgrading with a `docker-compose.yml` from before backups existed, the container
logs a warning at start and each backup fails with an error, rather than writing
archives inside the container where they would be lost when it is recreated.
Add `- ./backups:/backups` (or `${BW_BACKUP_PATH:-./backups}:/backups`) to the
service's volumes.

Archives are written mode `600` and owned by the user the application runs as
(`PUID`, 911 by default), so reading or copying them from the host needs `root`
or a matching user. See `BACKUP_FILE_MODE` below if that does not suit how you
copy them off-box.

## Supported databases

| `BW_DB_PROVIDER` | Backed up with | Supported |
| --- | --- | --- |
| `mysql`, `mariadb` | `mariadb-dump --single-transaction` | Yes, against MariaDB |
| `postgresql` | `pg_dump --format=custom` | Yes, PostgreSQL 18 and older |
| `sqlite` | `sqlite3 .backup` | Yes |
| `sqlserver` | — | No |

For unsupported providers the loop logs why and takes no backup, rather than
writing an archive that cannot restore. Back those up yourself, and include
`/etc/bitwarden` when you do.

A note on MySQL: the client in this image is MariaDB's, which cannot
authenticate to a stock MySQL 8 server — MySQL 8's default
`caching_sha2_password` plugin is not available in Alpine's MariaDB packages. If
`BW_DB_SERVER` points at a real MySQL 8 server rather than the bundled MariaDB,
back it up with your own tooling for now.

A note on PostgreSQL: the image ships the PostgreSQL 18 client, which can back
up and restore servers up to version 18. It refuses a newer server. A restore
runs in a single transaction, so a failure leaves the database unchanged.

## Configuration

All of these are set in `settings.env`. The schedule knobs are the same names,
defaults and meanings as the multi-container product, so the two read alike.

| Variable | Default | Meaning |
| --- | --- | --- |
| `BACKUP_DB` | `1` | Set to `0` to disable backups. |
| `BACKUP_INTERVAL` | `next day` | Passed to `date -d`. |
| `BACKUP_INTERVAL_FORMAT` | `%Y-%m-%d 00:00:00` | The point within the interval to run at — this is where you set the time of day. `%Y-%m-%d 03:00:00` runs at 03:00. |
| `BACKUP_TIMEZONE` | `UTC` | Timezone the schedule is interpreted in, e.g. `Europe/London`. |
| `BACKUP_ON_START` | `0` | Also back up immediately on container start. Not a substitute for a pre-upgrade backup; see [Before an upgrade](#before-an-upgrade). |
| `BACKUP_RETENTION_DAYS` | `32` | Delete archives older than this many days. `0` disables the sweep. |
| `BACKUP_FILE_MODE` | `600` | Mode of the finished archive. `640` if the process copying it off-box shares the container's group. |
| `BACKUP_INCLUDE_SETTINGS` | `false` | Include `settings.env` in each archive. Requires mounting it at `/settings.env`. |
| `BACKUP_DB_CLIENT_ARGS` | — | Connection options for every database client call, backup and restore alike, e.g. `--skip-ssl` for a MariaDB server without TLS. (PostgreSQL's `PG*` environment variables, such as `PGSSLMODE`, also work.) |
| `BACKUP_DB_EXTRA_ARGS` | — | Extra options for the dump only (`mariadb-dump` or `pg_dump`). The restore does not use them, so don't put connection options here. |

Old archives are only ever deleted **after** a new one has been written and
verified. A failed backup leaves the ones you already have alone.

To back up right now without waiting for the schedule:

```bash
docker compose exec --user bitwarden bitwarden /backup-db.sh
```

`--user bitwarden` runs the backup as the application user, so the archive gets
the same owner as scheduled backups (`PUID:PGID`). Without it, `docker compose
exec` runs as root and the archive is owned by root. On a rootless deployment,
leave `--user` out: the container already runs as your user, and has no
`bitwarden` account.

Backup activity is logged to `/var/log/bitwarden/backup.log`, and via
supervisord:

```bash
docker compose exec bitwarden supervisorctl status backup
```

## Before an upgrade

Take a backup with the **old** image still running, before pulling the new one:

```bash
docker compose exec --user bitwarden bitwarden /backup-db.sh
docker compose pull
docker compose up -d
```

`BACKUP_ON_START=1` does not do this for you. It runs when the *new* container
starts, after the new image has started migrating the database, so it is
neither a copy of the old version nor guaranteed to capture a finished schema.

## Restoring

A restore needs the application stopped and the config tree writable — and the
application *is* this container. So run a second container from the same image
with the restore script as its entrypoint:

```bash
docker compose stop bitwarden

docker compose run --rm --no-deps --entrypoint /restore-db.sh bitwarden \
  /backups/bitwarden-lite_20260915_030000.tar.gz --clean --yes

docker compose up -d
```

`--clean` replaces what is in the database: MariaDB drops and recreates it, and
PostgreSQL drops the objects in the dump before recreating them. Leave it off to
load into an empty database. `--yes` skips the confirmation prompt.

The restore unpacks the archive into a temporary directory beside it, so the
backups directory needs free space for the unpacked archive, roughly the
database plus your attachments. Set `RESTORE_STAGE_DIR` to unpack somewhere
else.

When the container starts, the application migrates the database to the
image's version. This applies after a restore from an older release, and
on a fresh install. During those first seconds the web vault and API can
already answer while requests that touch the database fail with a server error.
Give it a minute before treating an error as a failed restore.

Then do the only check that matters: **log in as a user who existed before the
backup, and open an item in their vault.** A restore that brings the stack up
but cannot decrypt is the failure mode this design exists to prevent, and it is
not visible from the container logs.

Two narrower modes exist for diagnosis, not for routine use:

- `--config-only` — restore `/etc/bitwarden` and leave the database alone.
- `--db-only` — restore the database and leave the key ring alone. **This does
  not produce a working instance**; the script warns before doing it.

## Restoring onto a different machine

Supported, with no extra configuration. The data-protection application
discriminator is pinned rather than derived from the host or install path, so an
archive unprotects correctly wherever you restore it. You need the archive and
your `settings.env`.

On a new machine nothing is running yet. With MariaDB or PostgreSQL, start the
database first so the restore has something to load into. The restore waits for
it to accept connections:

```bash
docker compose up -d db

docker compose run --rm --no-deps --entrypoint /restore-db.sh bitwarden \
  /backups/bitwarden-lite_20260915_030000.tar.gz --clean --yes

docker compose up -d
```

With SQLite, skip the first command.
