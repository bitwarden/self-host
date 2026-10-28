# syntax = docker/dockerfile:1.28
ARG SERVER_TAG=dev
ARG SERVER_REGISTRY=ghcr.io/bitwarden
ARG WEB_IMAGE=ghcr.io/bitwarden/web
ARG WEB_TAG=dev

###############################################
#              Web app stage                  #
###############################################
FROM ${WEB_IMAGE}:${WEB_TAG} AS web-app

###############################################
#              Server app stages              #
###############################################
FROM ${SERVER_REGISTRY}/admin:${SERVER_TAG} AS admin-app
FROM ${SERVER_REGISTRY}/api:${SERVER_TAG} AS api-app
FROM ${SERVER_REGISTRY}/events:${SERVER_TAG} AS events-app
FROM ${SERVER_REGISTRY}/icons:${SERVER_TAG} AS icons-app
FROM ${SERVER_REGISTRY}/identity:${SERVER_TAG} AS identity-app
FROM ${SERVER_REGISTRY}/notifications:${SERVER_TAG} AS notifications-app
FROM ${SERVER_REGISTRY}/scim:${SERVER_TAG} AS scim-app
FROM ${SERVER_REGISTRY}/sso:${SERVER_TAG} AS sso-app

###############################################
#                  App stage                  #
###############################################
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine3.23
ARG TARGETPLATFORM
LABEL com.bitwarden.product="bitwarden"
LABEL com.bitwarden.project="lite"
ENV ASPNETCORE_ENVIRONMENT=Production
ENV BW_ENABLE_IPV6=true
ENV BW_ENABLE_ADMIN=true
ENV BW_ENABLE_API=true
ENV BW_ENABLE_EVENTS=false
ENV BW_ENABLE_ICONS=true
ENV BW_ENABLE_IDENTITY=true
ENV BW_ENABLE_NOTIFICATIONS=true
ENV BW_ENABLE_SCIM=false
ENV BW_ENABLE_SSO=false
ENV BW_DB_FILE="/etc/bitwarden/vault.db"
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
ENV globalSettings__selfHosted="true"
ENV globalSettings__liteDeployment="true"
ENV globalSettings__pushRelayBaseUri="https://push.bitwarden.com"
ENV globalSettings__baseServiceUri__internalAdmin="http://localhost:5000"
ENV globalSettings__baseServiceUri__internalApi="http://localhost:5001"
ENV globalSettings__baseServiceUri__internalEvents="http://localhost:5003"
ENV globalSettings__baseServiceUri__internalIcons="http://localhost:5004"
ENV globalSettings__baseServiceUri__internalIdentity="http://localhost:5005"
ENV globalSettings__baseServiceUri__internalNotifications="http://localhost:5006"
ENV globalSettings__baseServiceUri__internalSso="http://localhost:5007"
ENV globalSettings__baseServiceUri__internalScim="http://localhost:5002"
ENV globalSettings__baseServiceUri__internalVault="http://localhost:8080"
ENV globalSettings__identityServer__certificatePassword="default_cert_password"
ENV globalSettings__dataProtection__directory="/etc/bitwarden/data-protection"
ENV globalSettings__attachment__baseDirectory="/etc/bitwarden/attachments"
ENV globalSettings__send__baseDirectory="/etc/bitwarden/attachments/send"
ENV globalSettings__licenseDirectory="/etc/bitwarden/licenses"
ENV globalSettings__logDirectoryByProject="false"
ENV globalSettings__logRollBySizeLimit="1073741824"

# Add packages
RUN apk add --no-cache \
    ca-certificates \
    curl \
    jq \
    nginx \
    openssl \
    supervisor \
    tzdata \
    unzip \
    su-exec \
    icu-libs \
    gcompat \
    mariadb-client \
    postgresql18-client \
    sqlite \
    coreutils \
    tar

# Microsoft's sqlcmd, for SQL Server backups. amd64 only: SQL Server itself has
# no ARM build. Not in Alpine's repos, so the packages are pinned by checksum
# (taken from files whose Microsoft GPG signatures verified).
ARG MSSQL_ODBC_URL=https://download.microsoft.com/download/ade174b7-8cea-4543-91a6-c33ae320c2f0/msodbcsql18_18.7.1.1-1_amd64.apk
ARG MSSQL_ODBC_SHA256=9ff10ec65dcbbe9ad57ed7fca648d8e7d09d4a25fc3c1095046f36a29f86b84a
ARG MSSQL_TOOLS_URL=https://download.microsoft.com/download/a5dcc5e7-6124-49d3-8df4-48d738a0e784/mssql-tools18_18.7.1.1-1_amd64.apk
ARG MSSQL_TOOLS_SHA256=f7aed935704f2ab8ed6c21a9bfc94a6135df0994b81f606a27629afbcc52cfe9
RUN if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
      cd /tmp \
      && curl --proto "=https" -fsSL -o msodbcsql18.apk "$MSSQL_ODBC_URL" \
      && curl --proto "=https" -fsSL -o mssql-tools18.apk "$MSSQL_TOOLS_URL" \
      && echo "$MSSQL_ODBC_SHA256  msodbcsql18.apk" | sha256sum -c - \
      && echo "$MSSQL_TOOLS_SHA256  mssql-tools18.apk" | sha256sum -c - \
      && apk add --no-cache --allow-untrusted msodbcsql18.apk mssql-tools18.apk \
      && rm msodbcsql18.apk mssql-tools18.apk; \
    fi

# Create required directories
RUN mkdir -p \
    /etc/bitwarden/attachments/send \
    /etc/bitwarden/data-protection \
    /etc/bitwarden/licenses \
    /etc/bitwarden/logs \
    /backups \
    /etc/supervisor \
    /etc/supervisor.d \
    /var/log/bitwarden \
    /app/Identity \
    /app/Sso \
    /app/Web

# Copy compiled apps from server images
WORKDIR /app
COPY --from=admin-app /app /app/Admin
COPY --from=api-app /app /app/Api
COPY --from=events-app /app /app/Events
COPY --from=icons-app /app /app/Icons
COPY --from=identity-app /app /app/Identity
COPY --from=notifications-app /app /app/Notifications
COPY --from=scim-app /app /app/Scim
COPY --from=sso-app /app /app/Sso

# Copy Web files from web-app stage
COPY --from=web-app /app /app/Web

# Create soft links for files generated in the entrypoint
RUN ln -sf /etc/bitwarden/identity.pfx /app/Identity/identity.pfx
RUN ln -sf /etc/bitwarden/identity.pfx /app/Sso/identity.pfx
RUN ln -sf /etc/bitwarden/Web/app-id.json /app/Web/app-id.json

# Set up supervisord
COPY bitwarden-lite/supervisord/*.ini /etc/supervisor.d/
COPY bitwarden-lite/supervisord/supervisord.conf /etc/supervisor/supervisord.conf
RUN rm -f /etc/supervisord.conf

# Set up nginx
COPY bitwarden-lite/nginx/nginx.conf /etc/nginx
COPY bitwarden-lite/nginx/proxy.conf /etc/nginx
COPY bitwarden-lite/nginx/mime.types /etc/nginx
COPY bitwarden-lite/nginx/security-headers.conf /etc/nginx
COPY bitwarden-lite/nginx/security-headers-ssl.conf /etc/nginx
COPY bitwarden-lite/nginx/logrotate.sh /

# Set up backups
COPY bitwarden-lite/backup/backup-db.sh /
COPY bitwarden-lite/backup/restore-db.sh /
RUN chmod +x /logrotate.sh

# Copy configuration templates
COPY bitwarden-lite/hbs/nginx-config.hbs /etc/hbs/
COPY bitwarden-lite/hbs/app-id.hbs /etc/hbs/
COPY bitwarden-lite/hbs/config.yaml /etc/hbs/

# Download hbs tool for generating final configurations
RUN echo "$(curl --silent https://api.github.com/repos/bitwarden/Handlebars.conf/git/refs/tags | jq -r 'last(.[].ref)' | sed 's/refs\/tags\///')"  > /tmp/latest.txt
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/amd64" ] ; then curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-x64.zip; fi
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/arm/v7" ] ; then curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm.zip; fi
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/arm64" ] ; then curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm64.zip; fi

# Extract hbs
RUN unzip hbs.zip -d /usr/local/bin && mv /usr/local/bin/hbs* /usr/local/bin/hbs && rm hbs.zip
RUN chmod +x /usr/local/bin/hbs

# Copy entrypoint script and make it executable
COPY bitwarden-lite/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 8080 8443
VOLUME ["/etc/bitwarden"]

WORKDIR /app
ENTRYPOINT ["/entrypoint.sh"]
