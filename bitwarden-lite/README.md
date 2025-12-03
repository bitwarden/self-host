# Bitwarden lite

Bitwarden lite is a streamlined, all-in-one deployment of Bitwarden for self-hosting. All Bitwarden services run in a single container with an embedded web server (nginx) and process manager (supervisor).

## Architecture Overview

Bitwarden lite consolidates multiple .NET services into a single container:

- **Admin** - Administrative portal
- **API** - Core API service
- **Events** - Event logging service
- **Icons** - Website icon fetching service
- **Identity** - Authentication service
- **Notifications** - Push notification service
- **SSO** - Single Sign-On service
- **SCIM** - User provisioning service
- **Web Vault** - Web client UI
- **nginx** - Reverse proxy and SSL termination

All services communicate internally via HTTP on localhost, with nginx providing a unified external interface.

## Quick Start

### Prerequisites

- Docker and Docker Compose
- Supported database: MariaDB, PostgreSQL, MySQL, MS SQL Server, or SQlite

### Basic Deployment

1. **Configure the Docker Compose file**
   ```bash
   curl -O https://raw.githubusercontent.com/bitwarden/self-host/refs/heads/main/bitwarden-lite/docker-compose.yml
   # Edit docker-compose.yml with your configuration
   ```

2. **Configure settings**
   ```bash
   curl -O https://raw.githubusercontent.com/bitwarden/self-host/refs/heads/main/bitwarden-lite/settings.env
   # Edit settings.env with your configuration
   ```

3. **Start services**
   ```bash
   docker compose up -d
   ```

4. **Access Bitwarden**
   - HTTP: http://localhost:80
   - HTTPS: https://localhost:443

## Configuration

### Environment Variables

#### Core Settings

| Variable | Default | Description |
|----------|---------|-------------|
| `BW_DOMAIN` | `localhost` | Domain name for your Bitwarden instance |
| `BW_PORT_HTTP` | `8080` | Internal HTTP port |
| `BW_PORT_HTTPS` | `8443` | Internal HTTPS port |
| `BW_ENABLE_SSL` | `true` | Enable SSL certificate generation |
| `BW_SSL_CERT` | `ssl.crt` | SSL certificate filename |
| `BW_SSL_KEY` | `ssl.key` | SSL private key filename |

#### Service Toggles

Enable or disable individual services:

| Variable | Default | Description |
|----------|---------|-------------|
| `BW_ENABLE_ADMIN` | `true` | Admin portal |
| `BW_ENABLE_API` | `true` | Core API |
| `BW_ENABLE_EVENTS` | `false` | Event logging |
| `BW_ENABLE_ICONS` | `true` | Icon service |
| `BW_ENABLE_IDENTITY` | `true` | Authentication |
| `BW_ENABLE_NOTIFICATIONS` | `true` | Push notifications |
| `BW_ENABLE_SSO` | `false` | Single Sign-On |
| `BW_ENABLE_SCIM` | `false` | User provisioning |

#### Database Configuration

| Variable | Required | Description |
|----------|----------|-------------|
| `BW_DB_PROVIDER` | Yes | Database type: `mysql`, `postgresql`, `sqlserver`, or `sqlite` |
| `BW_DB_SERVER` | Yes* | Database host (*not required for SQlite) |
| `BW_DB_DATABASE` | Yes | Database name |
| `BW_DB_USERNAME` | Yes* | Database user (*not required for SQlite) |
| `BW_DB_PASSWORD` | Yes* | Database password (*not required for SQlite) |
| `BW_DB_FILE` | `/etc/bitwarden/vault.db` | SQlite database file path |

#### User/Group Configuration

| Variable | Default | Description |
|----------|---------|-------------|
| `PUID` | `1000` | User ID for running services |
| `PGID` | `1000` | Group ID for running services |

### Global Settings

Additional configuration through environment variables with the `globalSettings__` prefix:

- `globalSettings__pushRelayBaseUri` - Push notification relay URL
- `globalSettings__identityServer__certificatePassword` - Certificate password (⚠️ change default!)
- `globalSettings__dataProtection__directory` - Data protection keys directory
- `globalSettings__attachment__baseDirectory` - File attachments directory
- `globalSettings__licenseDirectory` - License files directory

## Port Mapping

### External Ports (docker-compose.yml)

- `80` → `8080` (HTTP)
- `443` → `8443` (HTTPS)

## Health Monitoring

### Health Endpoint

- **URL**: `http://localhost:8080/alive`
- **Method**: GET
- **Success Response**: HTTP 200

### Docker Health Check

The container includes a built-in health check that polls the `/alive` endpoint every 30 seconds.

Check container health:
```bash
docker compose ps
docker inspect bitwarden-lite-bitwarden-1 | grep -A 10 Health
```

## Volumes

### Data Persistence

| Volume | Mount Point | Purpose |
|--------|-------------|---------|
| `bitwarden` | `/etc/bitwarden` | Configuration, certificates, database (SQlite), attachments |
| `logs` | `/var/log/bitwarden` | Application logs |
| `data` | Varies | Database data (MariaDB/PostgreSQL/MSSQL) |

### Important Files

- `/etc/bitwarden/vault.db` - SQlite database (if using SQlite)
- `/etc/bitwarden/ssl.crt` - SSL certificate
- `/etc/bitwarden/ssl.key` - SSL private key
- `/etc/bitwarden/identity.pfx` - Identity server certificate
- `/etc/bitwarden/attachments/` - File attachments
- `/etc/bitwarden/data-protection/` - ASP.NET data protection keys
- `/var/log/bitwarden/*.log` - Service logs

## Database Options

### SQlite (Default)

Simplest option for small deployments:

```yaml
env_file:
  - settings.env
```

```bash
# settings.env
BW_DB_PROVIDER=sqlite
BW_DB_FILE=/etc/bitwarden/vault.db
```

### MariaDB/MySQL

For production deployments:

```yaml
services:
  db:
    image: mariadb:10
    environment:
      MARIADB_USER: "bitwarden"
      MARIADB_PASSWORD: "<strong_password>"
      MARIADB_DATABASE: "bitwarden_vault"
      MARIADB_RANDOM_ROOT_PASSWORD: "true"
```

```bash
# settings.env
BW_DB_PROVIDER=mysql
BW_DB_SERVER=db
BW_DB_DATABASE=bitwarden_vault
BW_DB_USERNAME=bitwarden
BW_DB_PASSWORD=<strong_password>
```

### PostgreSQL

```yaml
services:
  db:
    image: postgres:14
    environment:
      POSTGRES_USER: "bitwarden"
      POSTGRES_PASSWORD: "<strong_password>"
      POSTGRES_DB: "bitwarden_vault"
```

```bash
# settings.env
BW_DB_PROVIDER=postgresql
BW_DB_SERVER=db
BW_DB_DATABASE=bitwarden_vault
BW_DB_USERNAME=bitwarden
BW_DB_PASSWORD=<strong_password>
```

### MS SQL Server

```yaml
services:
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      MSSQL_SA_PASSWORD: "<strong_password>"
      ACCEPT_EULA: "Y"
```

```bash
# settings.env
BW_DB_PROVIDER=sqlserver
BW_DB_SERVER=db
BW_DB_DATABASE=bitwarden_vault
BW_DB_USERNAME=sa
BW_DB_PASSWORD=<strong_password>
```

## SSL/TLS Configuration

### Auto-Generated Certificates

By default, Bitwarden lite generates a self-signed certificate on first startup:

```bash
BW_ENABLE_SSL=true
BW_DOMAIN=your-domain.com
```

Certificate is stored at `/etc/bitwarden/ssl.crt` and `/etc/bitwarden/ssl.key`.

### Custom Certificates

To use your own certificates:

1. Place certificate and key in the `bitwarden` volume
2. Configure environment variables:
   ```bash
   BW_SSL_CERT=your-cert.crt
   BW_SSL_KEY=your-key.key
   ```

### Let's Encrypt / Reverse Proxy

For production deployments, consider using:
- **Traefik** with automatic Let's Encrypt
- **nginx-proxy** with Let's Encrypt companion
- **Caddy** with automatic HTTPS

## Logs

### Viewing Logs

```bash
# All services
docker compose logs -f

# Specific service logs
docker exec bitwarden-lite-bitwarden-1 cat /var/log/bitwarden/api.log

# nginx logs
docker exec bitwarden-lite-bitwarden-1 cat /var/log/nginx/access.log
docker exec bitwarden-lite-bitwarden-1 cat /var/log/nginx/error.log
```

### Log Rotation

- **Supervisor logs**: Automatically rotated at 10MB, 5 backups kept
- **nginx logs**: Rotated daily by custom script, compressed after 1 day, deleted after 32 days

## Backup and Restore

### Backup

```bash
# Stop containers
docker compose down

# Backup volumes
docker run --rm -v bitwarden-lite_bitwarden:/data -v $(pwd):/backup alpine tar czf /backup/bitwarden-backup.tar.gz /data

# Backup database (if using external DB)
docker compose exec db mysqldump -u bitwarden -p bitwarden_vault > bitwarden-db-backup.sql

# Restart containers
docker compose up -d
```

### Restore

```bash
# Stop containers
docker compose down

# Restore volumes
docker run --rm -v bitwarden-lite_bitwarden:/data -v $(pwd):/backup alpine sh -c "cd / && tar xzf /backup/bitwarden-backup.tar.gz"

# Restore database (if using external DB)
docker compose exec -T db mysql -u bitwarden -p bitwarden_vault < bitwarden-db-backup.sql

# Restart containers
docker compose up -d
```

## Upgrading

```bash
# Pull latest image
docker compose pull

# Restart with new image
docker compose up -d
```

Database migrations run automatically on startup.

## Support

- **Documentation**: https://bitwarden.com/help/
- **Community**: https://community.bitwarden.com/
- **Issues**: https://github.com/bitwarden/server/issues/2480

## License

Copyright © Bitwarden Inc. - See LICENSE file for details.
