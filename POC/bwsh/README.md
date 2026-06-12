# bwsh — Bitwarden self-host CLI

A single .NET tool that installs and manages a Bitwarden self-host deployment — replacing the
`bitwarden.sh` + `run.sh` + Setup-container stack. It generates config in-process and brings the
stack up: **standard** drives the Docker Engine API directly, while **lite** runs the repo's
`bitwarden-lite/docker-compose.yml` via `docker compose`.

![bwsh status](docs/images/status.gif)

> Status: working proof of concept. See **What's not done yet** at the bottom.

## Prerequisites

- **.NET 10 SDK** — https://dotnet.microsoft.com/download
- **Docker** running (lite deployments also need the `docker compose` CLI plugin)
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
dotnet run -- config                 # print the current config (secrets redacted); config key=value to set
dotnet run -- logs identity          # a service's logs; --export bundles all to a zip
dotnet run -- update                 # pull latest images and recreate changed services
dotnet run -- backup                 # snapshot config + secrets + database to a .tar.gz
dotnet run -- restore backup.tar.gz  # restore from a backup
dotnet run -- uninstall              # stop and remove; --purge also deletes data
```

Run `dotnet run -- <command> --help` for options on any command.

## HTTPS

Both standard and lite installs serve **HTTPS by default** with a generated self-signed cert. On standard, provide your own certificate by placing the
files in `bwdata/ssl/<domain>/` before installing:

```text
bwdata/ssl/bitwarden.example.com/certificate.crt
bwdata/ssl/bitwarden.example.com/private.key
bwdata/ssl/bitwarden.example.com/ca.crt        # optional
bwdata/ssl/bitwarden.example.com/dhparam.pem   # optional
```

On lite, drop `ssl.crt` and `ssl.key` into the data dir before installing and the container uses those
instead of self-signing (point at other filenames with `BW_SSL_CERT`/`BW_SSL_KEY`).

For a trusted cert on standard, use Let's Encrypt (needs a public domain with ports 80/443 reachable from
the internet):

```yaml
ssl:
  enable: true
  lets-encrypt: true
  email: you@example.com
```

`install`/`apply` then provisions the cert with certbot. Renew with `dotnet run -- renewcert` (cron it for
auto-renewal).

Ports default to 80/443. On standard, override with `http-port`/`https-port` in the manifest; lite
serves on 80/443 from the upstream compose and ignores those fields. Opt out of HTTPS with
`ssl: { enable: false }`.

## Shell completion

`bwsh` prints a completion script (zsh or bash) that tab-completes commands, options, deployment
kinds (`standard`/`lite`), and service names. Load it with the `bwsh` binary on your PATH:

```bash
source <(bwsh completions zsh)                 # zsh, current shell
bwsh completions zsh > "${fpath[1]}/_bwsh"     # zsh, persistent (restart zsh)

source <(bwsh completions bash)                # bash, current shell
bwsh completions bash > /usr/local/etc/bash_completion.d/bwsh   # bash, persistent
```

It delegates back to `bwsh` for suggestions, so no `dotnet-suggest` tool is needed.

## Migrate an existing bash install

Adopt a stack that was installed with `bitwarden.sh` under CLI management — non-destructive,
backs up first:

```bash
dotnet run -- migrate --root ./bwdata
```

## Local development

A development manifest [`bitwarden.yaml`](bitwarden.yaml) can be used for local testing. It
installs a `standard` deployment on `localhost` and points outbound mail at a local MailDev server.

```yaml
deployment: standard
domain: localhost
region: US
installation-id: 94389b62-6b3f-413e-8bbb-6c9e4ed83cb3
installation-key: bwsh-demo
database: vault
config:
  # Containers reach MailDev on the Docker host. View mail at http://localhost:1080
  globalSettings__mail__smtp__host: host.docker.internal
  globalSettings__mail__smtp__port: "1025"
  globalSettings__mail__smtp__ssl: "false"
  globalSettings__mail__smtp__startTls: "false"
  globalSettings__mail__smtp__username: ""
  globalSettings__mail__smtp__password: ""
```

### Mail server (MailDev)

Start the MailDev server with docker compose.

```bash
cd ../maildev
docker compose up -d        # start (pulls maildev/maildev on first run)
docker compose logs -f      # tail
docker compose down         # stop
```

### Install against the test manifest

```bash
dotnet run -- install --manifest bitwarden.yaml
```

Register a user, then open http://localhost:1080 to see the welcome email. If you change the
`config:` block later, re-apply with `dotnet run -- apply --manifest bitwarden.yaml` and restart.

## Notes

- Data lives in `./bwdata` by default; override with `--root <dir>`.
- Versions default to the pinned `version.json` release; override per run with
  `update --core-version <v> --web-version <v>` or in the manifest.
- A real `bwsh` binary can be produced with `dotnet publish -c Release --self-contained`.
- The demo GIFs are generated from VHS tapes in [`docs/`](docs/README.md).

## What's not done yet

Installation id/key cloud validation · signed-binary distribution & self-update ·
Let's Encrypt / `renewcert` · Windows deployment parity · cloud marketplace channels.
