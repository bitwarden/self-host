# bwsh — Bitwarden self-host CLI

A single .NET tool that installs and manages a Bitwarden self-host deployment — replacing the
`bitwarden.sh` + `run.sh` + Setup-container stack. It generates all config in-process and drives
Docker directly. Supports both **standard** and **lite** deployments.

![bwsh status](docs/images/status.gif)

> Status: working proof of concept. See **What's not done yet** at the bottom.

## Prerequisites

- **.NET 10 SDK** — https://dotnet.microsoft.com/download
- **Docker** running
- An installation **ID + key** from https://bitwarden.com/host

## Install

From this directory, the tool runs via `dotnet run --`.

### Interactive

```bash
dotnet run -- install
```

It asks for your domain, region, installation id/key, and database name, then pulls the images,
starts the stack, and shows a live status table.

![bwsh install](docs/images/install.gif)

### Unattended with a manifest

Create `bitwarden.yaml`:

```yaml
deployment: standard            # standard | lite
domain: bitwarden.example.com
region: US                      # US | EU
installation-id: 00000000-0000-0000-0000-000000000000
installation-key: your-key-here
# optional:
# enable-key-connector: true
# enable-scim: true
```

Then:

```bash
dotnet run -- install --manifest bitwarden.yaml
```

Add `--plan` to preview without pulling or starting anything.

### Apply manifest changes

Manifests can be re-applied to an existing install to change config or toggle services on/off.

```bash
dotnet run -- apply --manifest bitwarden.yaml
```

This is how you change config (e.g. add SMTP under the manifest's `config:` block) on an existing install.

## Everyday commands

```bash
dotnet run -- status                 # health, versions, and vault URL
dotnet run -- logs identity          # a service's logs; --export bundles all to a zip
dotnet run -- update                 # pull latest images and recreate changed services
dotnet run -- backup                 # snapshot config + secrets + database to a .tar.gz
dotnet run -- restore backup.tar.gz  # restore from a backup
dotnet run -- uninstall              # stop and remove; --purge also deletes data
```

Run `dotnet run -- <command> --help` for options on any command.

## Migrate an existing bash install

Adopt a stack that was installed with `bitwarden.sh` under CLI management — non-destructive,
backs up first:

```bash
dotnet run -- migrate --root ./bwdata
```

## Notes

- Data lives in `./bwdata` by default; override with `--root <dir>`.
- Versions default to the pinned `version.json` release; override per run with
  `update --core-version <v> --web-version <v>` or in the manifest.
- A real `bwsh` binary can be produced with `dotnet publish -c Release --self-contained`.
- The demo GIFs are generated from VHS tapes in [`docs/`](docs/README.md).

## What's not done yet

Installation id/key cloud validation · signed-binary distribution & self-update ·
Let's Encrypt / `renewcert` · Windows deployment parity · cloud marketplace channels.
