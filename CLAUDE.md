# Bitwarden Self-Host - Claude Code Configuration

Bitwarden's self-host release repository — a deployment and release orchestration hub that aggregates versions from upstream repos, publishes Docker images to `ghcr.io`, and provides installation tooling for multiple deployment models.

## Overview

### What This Project Does

- **Primary function**: Orchestrates releases and deployments for Bitwarden's self-hosted product, packaging upstream application code (`bitwarden/server`, `bitwarden/clients`, `bitwarden/key-connector`) into Docker images and cloud marketplace VM images.
- **Key interfaces**: `bitwarden.sh`/`bitwarden.ps1` (installation CLI), `run.sh`/`run.ps1` (Docker Compose orchestration), GitHub Actions workflows (CI/CD), Bitwarden Lite Docker container (all-in-one deployment).
- **Target users**: Self-hosted Bitwarden administrators, Bitwarden release engineers, cloud marketplace consumers.

### Key Concepts

- **Traditional deployment**: Multi-container Docker Compose setup orchestrated by shell/PowerShell scripts
- **Bitwarden Lite**: Single all-in-one Docker container using supervisord to run all .NET services + Nginx
- **Marketplace images**: Pre-built VM images (AMI, Azure SIG, DO droplet) for one-click cloud deployments via Packer
- **Upstream versions**: This repo does not contain application source code — it tracks `coreVersion` (server), `webVersion` (clients), and `keyConnectorVersion` from upstream repos
- **Cosign signing**: All container images are signed with keyless Cosign (OIDC via GitHub Actions) for supply chain integrity
- **Calendar versioning**: Versions follow `YYYY.MM.PATCH` format (e.g., `2026.3.1`)

---

## Architecture & Patterns

### System Architecture

```
Upstream Repos (bitwarden/server, bitwarden/clients, bitwarden/key-connector)
         |
         | version.json tracks upstream versions
         v
    Release Pipeline (release.yml)
         |
    ┌────┴────────────────────┬──────────────────────┐
    |                         |                      |
    v                         v                      v
 Traditional              Bitwarden Lite         Marketplace Images
 (14 Docker images        (single container)     (Packer-built VMs)
  pushed to ghcr.io)           |                      |
    |                         |              ┌────────┼────────┐
    v                         v              v        v        v
 bitwarden.sh/ps1     docker-compose.yml    AWS    Azure    DigitalOcean
 + run.sh/ps1         + settings.env        AMI    SIG      Droplet
 + Docker Compose     + entrypoint.sh
```

### Code Organization

```
self-host/
├── .github/
│   ├── workflows/             # CI/CD pipelines
│   │   ├── release.yml        # Main release orchestration
│   │   ├── build-bitwarden-lite.yml  # Lite image build (reusable)
│   │   ├── release-aws.yml    # AWS Marketplace
│   │   ├── release-azure.yml  # Azure Marketplace
│   │   ├── release-digital-ocean.yml # DO Marketplace
│   │   ├── scan.yml           # SAST + Sonar
│   │   └── cleanup-container-images.yml  # Image cleanup
│   └── CODEOWNERS
├── bitwarden-lite/            # All-in-one container
│   ├── Dockerfile             # Multi-stage build (296 lines)
│   ├── docker-compose.yml     # Local development compose
│   ├── entrypoint.sh          # Container init (certs, DB, services)
│   ├── settings.env           # Configuration template
│   ├── .env.example           # Compose env vars
│   ├── supervisord/           # Process manager configs
│   │   ├── supervisord.conf
│   │   └── *.ini              # Per-service configs (admin, api, etc.)
│   ├── nginx/                 # Reverse proxy configs
│   └── hbs/                   # Handlebars templates for config generation
├── CommonMarketplace/         # Shared marketplace setup
│   ├── scripts/               # VM provisioning (UFW, cleanup, first-run)
│   └── files/                 # Installed files (MOTD, cron, cloud-init)
├── AWSMarketplace/            # AWS-specific Packer + validation
├── AzureMarketplace/          # Azure-specific Packer + validation
├── DigitalOceanMarketplace/   # DO-specific validation
├── bitwarden.sh               # Linux/macOS installation CLI
├── bitwarden.ps1              # Windows installation CLI
├── run.sh                     # Linux/macOS Docker Compose orchestrator
├── run.ps1                    # Windows Docker Compose orchestrator
└── version.json               # Upstream version manifest
```

### Key Principles

1. **No application source code**: This repo only packages and deploys — application logic lives in `bitwarden/server` and `bitwarden/clients`
2. **Platform parity**: Bash and PowerShell scripts maintain feature parity (`bitwarden.sh`/`bitwarden.ps1`, `run.sh`/`run.ps1`)
3. **Supply chain security**: All container images are Cosign-signed; marketplace images are validated with platform-specific check scripts
4. **Configuration-driven**: Services are toggled via `BW_ENABLE_*` environment variables, not code changes

### Core Patterns

#### Marketplace Image Build Pattern

**Purpose**: Consistent VM image creation across cloud providers using Packer with shared provisioning scripts.

**Implementation**: Each marketplace has a `.pkr.hcl` file that references `CommonMarketplace/` for shared setup, then runs platform-specific validation.

```
CommonMarketplace/scripts/01-setup-first-run.sh  # Create user, set permissions
CommonMarketplace/scripts/02-ufw-bitwarden.sh    # Firewall rules
CommonMarketplace/scripts/90-cleanup.sh           # Sanitize image
{Platform}Marketplace/scripts/99-img-check.sh     # Platform-specific validation
```

#### Service Enablement Pattern

**Purpose**: Toggle individual .NET services in Bitwarden Lite without rebuilding the container.

**Implementation** (`bitwarden-lite/entrypoint.sh`):

```bash
# Services controlled by BW_ENABLE_* env vars in settings.env
# entrypoint.sh reads these and enables/disables supervisord .ini files
if [ "$BW_ENABLE_ADMIN" = "true" ]; then
    # Enable admin.ini in supervisord
fi
```

#### Version Tracking Pattern

**Purpose**: Single source of truth for upstream dependency versions.

**Implementation** (`version.json`):

```json
{
  "coreVersion": "2026.3.1",
  "webVersion": "2026.3.0",
  "keyConnectorVersion": "2025.11.0"
}
```

The release workflow fetches latest versions from upstream repos, updates this file, and propagates versions to `bitwarden.sh`, `bitwarden.ps1`, and Docker image tags.

---

## Development Guide

### Adding a New Marketplace Platform

**1. Create Platform Directory**

```
{Platform}Marketplace/
├── marketplace-image.pkr.hcl    # Packer template
└── scripts/
    └── 99-img-check.sh          # Platform validation
```

**2. Write Packer Template** (`{Platform}Marketplace/marketplace-image.pkr.hcl`)

Reference `CommonMarketplace/` for shared provisioning:

```hcl
build {
  provisioner "shell" {
    scripts = [
      "${path.root}/../CommonMarketplace/scripts/01-setup-first-run.sh",
      "${path.root}/../CommonMarketplace/scripts/02-ufw-bitwarden.sh",
      "${path.root}/../CommonMarketplace/scripts/90-cleanup.sh",
      "${path.root}/scripts/99-img-check.sh"
    ]
  }
}
```

**3. Write Validation Script** (`scripts/99-img-check.sh`)

Check platform requirements: OS version, cloud-init, Docker, firewall, no SSH keys, no bash history, no logs.

**4. Create Release Workflow** (`.github/workflows/release-{platform}.yml`)

- Triggered by release event or manual dispatch
- Runs Packer build with platform credentials
- Reference existing workflows (`release-aws.yml`, `release-azure.yml`) as templates

**5. Update CODEOWNERS** (`.github/CODEOWNERS`)

Add ownership entry for the new directory.

### Adding a New Service to Bitwarden Lite

**1. Add supervisord config** (`bitwarden-lite/supervisord/{service}.ini`)

```ini
[program:{service}]
command=/app/{Service}/{Service}.dll
directory=/app/{Service}
autostart=true
autorestart=true
priority=20
stdout_logfile=/var/log/bitwarden/{service}.log
stdout_logfile_maxbytes=10MB
stdout_logfile_backups=5
redirect_stderr=true
```

**2. Update Dockerfile** (`bitwarden-lite/Dockerfile`)

Add build stage for the new .NET service and copy artifacts.

**3. Update entrypoint.sh** (`bitwarden-lite/entrypoint.sh`)

Add `BW_ENABLE_{SERVICE}` toggle logic.

**4. Update settings.env** (`bitwarden-lite/settings.env`)

Add the new `BW_ENABLE_{SERVICE}` variable with default value.

**5. Update nginx config** if the service needs HTTP routing (`bitwarden-lite/nginx/`).

### Modifying Installation Scripts

When editing `bitwarden.sh` or `bitwarden.ps1`:

- Keep both scripts in sync — changes to one usually require the same change to the other
- Version strings are embedded directly in these scripts (updated by `release.yml`)
- Test both `docker compose` (plugin) and `docker-compose` (standalone) code paths in `bitwarden.sh`

---

## Data Models

### Core Types

This repo has no application data models. The key data structure is `version.json`:

```json
{
  "coreVersion": "YYYY.MM.PATCH",
  "webVersion": "YYYY.MM.PATCH",
  "keyConnectorVersion": "YYYY.MM.PATCH"
}
```

### Configuration Schema

**`settings.env`** — flat key-value environment file consumed by `docker-compose.yml` and `entrypoint.sh`. No formal schema; validated at runtime by the application services.

**Packer variables** — defined in `.pkr.hcl` files with HCL variable blocks and sourced from environment or Azure Key Vault.

---

## Security & Configuration

### Security Rules

**MANDATORY - These rules have no exceptions:**

1. **Never hardcode secrets**: All secrets flow through Azure Key Vault or GitHub Secrets — never commit credentials, API keys, or passwords to the repository
2. **Sign all container images**: Every image pushed to `ghcr.io` must be signed with Cosign keyless signing. Verification: `cosign verify ghcr.io/bitwarden/$IMAGE:latest --certificate-identity-regexp="https://github\.com/bitwarden/self-host/\.github/workflows/release\.yml@.*" --certificate-oidc-issuer="https://token.actions.githubusercontent.com"`
3. **Sanitize marketplace images**: All marketplace VM images must pass platform-specific validation (`99-img-check.sh`) ensuring no SSH keys, bash history, or logs remain
4. **Use parameterized inputs**: Shell scripts must not interpolate untrusted input into commands — use proper quoting and parameterization
5. **Minimal container privileges**: Bitwarden Lite runs services as a non-root user (configurable via `PUID`/`PGID`)

### Security Functions

| Function/Tool | Purpose | Usage |
|---------------|---------|-------|
| Cosign keyless signing | Container image integrity | Runs in `release.yml` after every image push |
| Anchore/Grype scanning | Container vulnerability detection | Runs in `build-bitwarden-lite.yml`, uploads SARIF |
| Checkmarx SAST | Static application security testing | Runs in `scan.yml` on every push/PR |
| UFW firewall setup | VM network hardening | `CommonMarketplace/scripts/02-ufw-bitwarden.sh` |
| Image validation scripts | Marketplace image sanitization | `{Platform}Marketplace/scripts/99-img-check.sh` |

### Environment Configuration

**Required Variables (`settings.env`):**

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `BW_DOMAIN` | Yes | Server hostname | `bitwarden.example.com` |
| `BW_DB_PROVIDER` | Yes | Database type | `mysql`, `postgresql`, `sqlserver`, `sqlite` |
| `BW_DB_SERVER` | Yes* | Database host | `db` |
| `BW_DB_DATABASE` | Yes* | Database name | `bitwarden_vault` |
| `BW_DB_USERNAME` | Yes* | Database user | `bitwarden` |
| `BW_DB_PASSWORD` | Yes* | Database password | (set securely) |
| `BW_INSTALLATION_ID` | Yes | UUID from bitwarden.com/host | UUID format |
| `BW_INSTALLATION_KEY` | Yes | License key | (from bitwarden.com/host) |

*Not required when `BW_DB_PROVIDER=sqlite`

**Service Toggle Variables:**

| Variable | Default | Description |
|----------|---------|-------------|
| `BW_ENABLE_ADMIN` | `true` | Admin portal |
| `BW_ENABLE_API` | `true` | Core API |
| `BW_ENABLE_EVENTS` | `false` | Audit event logging |
| `BW_ENABLE_ICONS` | `true` | Icon cache service |
| `BW_ENABLE_IDENTITY` | `true` | SSO/OIDC provider |
| `BW_ENABLE_NOTIFICATIONS` | `true` | Real-time push notifications |
| `BW_ENABLE_SCIM` | `false` | SCIM directory provisioning |
| `BW_ENABLE_SSO` | `false` | SAML/OIDC authentication |

**SSL/TLS Variables:**

| Variable | Default | Description |
|----------|---------|-------------|
| `BW_ENABLE_SSL` | `false` | Enable HTTPS termination |
| `BW_SSL_CERT` | — | Certificate filename |
| `BW_SSL_KEY` | — | Private key filename |
| `BW_ENABLE_SSL_CA` | `false` | Enable custom CA |
| `BW_SSL_CA_CERT` | — | CA certificate filename |

**CI/CD Secrets (Azure Key Vault):**

| Secret | Purpose |
|--------|---------|
| `BW-GHAPP-ID` / `BW-GHAPP-KEY` | GitHub App credentials for release automation |
| `aws-selfhost-version-access-id` / `access-key` | S3 bucket access for version.json upload |
| `aws-selfhost-version-bucket-name` | S3 bucket name |
| `azure-marketplace-subscription-id` | Azure marketplace image builds |

### Authentication & Authorization

- **Container image signing**: Cosign keyless via GitHub OIDC — no static keys, identity tied to workflow run
- **Marketplace VM access**: SSH keys removed during image build; access provisioned by cloud platform on deployment
- **Self-signed certificates**: `entrypoint.sh` generates RSA 4096-bit certs for Identity Server and optional SSL on first run
- **Random key generation**: `INTERNAL_IDENTITY_KEY`, `OIDC_IDENTITY_CLIENT_KEY`, and `DUO_AKEY` are generated via `openssl rand -hex 30` at container startup

---

## Testing

### Test Structure

This repository has **no unit or integration tests**. Quality assurance is handled through:

```
Testing Strategy:
├── SAST (Checkmarx)           # Static analysis via scan.yml
├── Code Quality (Sonar)       # Code quality via scan.yml
├── Container Scanning (Grype) # Vulnerability scanning in build-bitwarden-lite.yml
└── Image Validation           # Platform-specific 99-img-check.sh scripts
```

### Running Tests

**SAST + Sonar** — triggered automatically on push/PR to `main` via `scan.yml`

**Container vulnerability scanning** — runs during Bitwarden Lite builds:

```bash
# Scanning is automated in CI, but can be run locally:
docker buildx build --platform linux/amd64 -t bitwarden/lite:local bitwarden-lite/
grype bitwarden/lite:local
```

**Marketplace image validation** — run platform check scripts during Packer build:

```bash
# Example: validate an AWS marketplace image
bash AWSMarketplace/scripts/99-img-check.sh
```

### Test Environment

- No local test environment setup required
- CI runs in GitHub Actions with Ubuntu runners
- Marketplace builds use platform-specific VMs (t3.small for AWS, Standard_B2s for Azure)

---

## Code Style & Standards

### Formatting

- **Indentation**: 4 spaces for shell/PowerShell scripts, 2 spaces for JSON/YAML
- **Line endings**: LF (Unix-style)
- **Encoding**: UTF-8
- **Line length**: 120-character guideline

### Naming Conventions

- `kebab-case` for: workflow files, Docker image names, directory names
- `camelCase` for: JSON keys in `version.json`
- `SCREAMING_SNAKE_CASE` for: environment variables (`BW_DOMAIN`, `BW_ENABLE_*`)
- `PascalCase` for: .NET service names in Dockerfile (`Admin`, `Api`, `Identity`)

### Imports

Not applicable — this repo contains shell scripts, Dockerfiles, and YAML workflows, not compiled source code.

### Comments

- Use `#` comments in shell scripts and YAML to explain non-obvious logic
- Include inline comments in `settings.env` to document each variable group

### Pre-commit Hooks

No pre-commit hooks configured. Quality gates run in CI:
- Checkmarx SAST on push/PR
- Sonar code quality on push/PR

---

## Anti-Patterns

### DO

- Keep `bitwarden.sh` and `bitwarden.ps1` in sync when making changes to either
- Use `CommonMarketplace/` for shared provisioning logic across cloud platforms
- Sign all container images with Cosign in release workflows
- Run platform-specific validation scripts (`99-img-check.sh`) in marketplace builds
- Use Azure Key Vault for all CI/CD secrets
- Test Docker builds locally with `docker buildx build` before pushing workflow changes
- Update `version.json` through the release workflow, not manually
- Match existing script style when editing shell scripts

### DON'T

- Commit secrets, API keys, or credentials to the repository
- Manually edit version strings in `bitwarden.sh`/`bitwarden.ps1` — the release workflow manages these
- Skip Cosign signing when adding new container image pushes
- Add application source code to this repo — it belongs in upstream repos
- Remove or weaken marketplace image sanitization checks (SSH keys, bash history, logs)
- Use `docker-compose` (v1) syntax without also supporting `docker compose` (v2 plugin) in scripts
- Create marketplace images without running the corresponding `99-img-check.sh` validation
- Hardcode platform-specific logic in `CommonMarketplace/` — use platform directories instead

---

## Deployment

### Building

**Bitwarden Lite (local):**

```bash
cd bitwarden-lite/
docker buildx build --platform linux/amd64 -t bitwarden/lite:local .
```

**Running locally:**

```bash
cd bitwarden-lite/
# 1. Copy and edit settings.env
# 2. Start services
docker-compose up -d
docker-compose logs -f bitwarden
docker-compose down
```

**Traditional deployment:**

```bash
./bitwarden.sh install   # Interactive setup (domain, SSL, DB)
./bitwarden.sh start
./bitwarden.sh stop
./bitwarden.sh restart
./bitwarden.sh update    # Pull latest images
```

### Versioning

Calendar versioning: `YYYY.MM.PATCH` (e.g., `2026.3.1`)

- Tracks three upstream versions in `version.json`: `coreVersion`, `webVersion`, `keyConnectorVersion`
- Release workflow auto-updates version strings across the repo

### Publishing

Release is fully automated via `release.yml` (manual dispatch):

1. Validates version not already released
2. Fetches latest upstream versions, updates `version.json` and scripts
3. Creates GitHub release with manifests
4. Pushes 14 Docker images to `ghcr.io/bitwarden` with Cosign signing
5. Uploads `version.json` to S3
6. Triggers marketplace workflows (AWS, Azure, DigitalOcean)

---

## Troubleshooting

### Common Issues

#### Docker Compose Version Compatibility

**Problem**: Scripts fail because `docker compose` (v2 plugin) or `docker-compose` (v1 standalone) is not found.

**Solution**: `bitwarden.sh` includes fallback logic — ensure at least one is installed. Check with `docker compose version` or `docker-compose --version`.

#### Bitwarden Lite Services Not Starting

**Problem**: A service fails to start in the Lite container.

**Solution**: Check supervisord logs at `/var/log/bitwarden/{service}.log`. Verify the corresponding `BW_ENABLE_*` variable is set to `true` in `settings.env`. Inspect entrypoint behavior with `docker-compose logs bitwarden`.

#### Marketplace Image Validation Failures

**Problem**: `99-img-check.sh` fails during Packer build.

**Solution**: Common failures include: leftover SSH keys (`/root/.ssh/authorized_keys`), uncleared bash history, remaining log files, or wrong OS version. Review the specific check that failed and fix the provisioning scripts in `CommonMarketplace/scripts/`.

#### SSL Certificate Issues

**Problem**: HTTPS not working in Bitwarden Lite.

**Solution**: Verify `BW_ENABLE_SSL=true` in `settings.env` and that certificate/key files are mounted at the expected paths. `entrypoint.sh` generates self-signed certs if none exist — check `/etc/bitwarden/ssl/` for generated files.

### Debug Tips

- **Bitwarden Lite logs**: `docker-compose logs -f bitwarden` or inspect `/var/log/bitwarden/` inside the container
- **Supervisord status**: `docker exec <container> supervisorctl status` to check individual service states
- **Entrypoint debugging**: Add `set -x` to `entrypoint.sh` for verbose shell tracing
- **Packer builds**: Use `PACKER_LOG=1` environment variable for verbose Packer output
- **Workflow debugging**: Use `act` or check GitHub Actions run logs for CI/CD issues

---

## References

### Official Documentation

- [Bitwarden Self-Host Guide](https://bitwarden.com/help/self-host-an-organization/)
- [Bitwarden Installation & Setup](https://bitwarden.com/help/install-on-premise-linux/)

### Internal Documentation

- [CONTRIBUTING.md](./CONTRIBUTING.md) — Contribution guidelines
- [SECURITY.md](./SECURITY.md) — Security vulnerability reporting

### Tools & Libraries

- [Docker Buildx](https://docs.docker.com/buildx/working-with-buildx/) — Multi-platform image builds
- [Cosign](https://docs.sigstore.dev/cosign/overview/) — Container image signing
- [Hashicorp Packer](https://www.packer.io/docs) — Marketplace VM image building
- [Supervisord](http://supervisord.org/) — Process management in Bitwarden Lite
- [Handlebars](https://handlebarsjs.com/) — Template engine for config generation

### Team Ownership

- **Default**: `@bitwarden/dept-shot` (Self-Host team)
- **Docker/security files**: Also `@bitwarden/team-appsec`
- **Release workflows**: `@bitwarden/dept-bre` (Release Engineering)
