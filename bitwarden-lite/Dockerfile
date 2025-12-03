# syntax = docker/dockerfile:1.20
###############################################
#                 Build stage                 #
###############################################
FROM --platform=$BUILDPLATFORM alpine:3.21 AS web-setup
ARG WEB_ARTIFACT_PATH

# Add packages
RUN apk add --no-cache \
    curl \
    git \
    jq \
    unzip

WORKDIR /tmp

# Grab last tag/release and download the 'web' client
RUN if [ -z "${WEB_ARTIFACT_PATH}" ]; then \
      TAG=$(git ls-remote --tags https://github.com/bitwarden/clients.git \
        | grep -E 'refs/tags/web-v[0-9]{4}\.([1-9]|1[0-2])\.[0-9]+' \
        | cut -d/ -f3 | sort -Vr | head -1) \
      && VERSION=$(echo "$TAG" | grep -o -E '[0-9]{4}\.([1-9]|1[0-2])\.[0-9]+') \
      && curl --proto "=https" -L https://github.com/bitwarden/clients/releases/download/$TAG/web-$VERSION-selfhosted-COMMERCIAL.zip -O; \
    fi

# Copy provided web artifact if available
COPY ${WEB_ARTIFACT_PATH}* /tmp/

# Unzip the 'web' client to /tmp/build
RUN if [ -z "${WEB_ARTIFACT_PATH}" ]; then \
      unzip web-*-selfhosted-COMMERCIAL.zip; \
    else \
      unzip ${WEB_ARTIFACT_PATH} -d /tmp/; \
    fi

###############################################
#                 Build stage                 #
###############################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0-alpine3.21 AS dotnet-build

# Docker buildx supplies the value for this arg
ARG TARGETPLATFORM

# Determine proper runtime value for .NET
# We put the value in a file to be read by later layers.
RUN case "$TARGETPLATFORM" in \
      "linux/amd64") RID=linux-musl-x64 ;; \
      "linux/arm64") RID=linux-musl-arm64 ;; \
      "linux/arm/v7") RID=linux-musl-arm ;; \
    esac \
    && echo "RID=$RID" > /tmp/rid.txt

# Add packages
RUN apk add --no-cache npm

# Copy csproj files as distinct layers
WORKDIR /source
COPY server/src/Admin/*.csproj ./src/Admin/
COPY server/src/Api/*.csproj ./src/Api/
COPY server/src/Events/*.csproj ./src/Events/
COPY server/src/Icons/*.csproj ./src/Icons/
COPY server/src/Identity/*.csproj ./src/Identity/
COPY server/src/Notifications/*.csproj ./src/Notifications/
COPY server/bitwarden_license/src/Sso/*.csproj ./bitwarden_license/src/Sso/
COPY server/bitwarden_license/src/Scim/*.csproj ./bitwarden_license/src/Scim/
COPY server/src/Core/*.csproj ./src/Core/
COPY server/src/Infrastructure.Dapper/*.csproj ./src/Infrastructure.Dapper/
COPY server/src/Infrastructure.EntityFramework/*.csproj ./src/Infrastructure.EntityFramework/
COPY server/src/SharedWeb/*.csproj ./src/SharedWeb/
COPY server/util/Migrator/*.csproj ./util/Migrator/
COPY server/util/MySqlMigrations/*.csproj ./util/MySqlMigrations/
COPY server/util/PostgresMigrations/*.csproj ./util/PostgresMigrations/
COPY server/util/SqliteMigrations/*.csproj ./util/SqliteMigrations/
COPY server/bitwarden_license/src/Commercial.Core/*.csproj ./bitwarden_license/src/Commercial.Core/
COPY server/bitwarden_license/src/Commercial.Infrastructure.EntityFramework/*.csproj ./bitwarden_license/src/Commercial.Infrastructure.EntityFramework/
COPY server/Directory.Build.props .
COPY server/.editorconfig .

# Restore Admin project dependencies and tools
WORKDIR /source/src/Admin
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Api project dependencies and tools
WORKDIR /source/src/Api
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Events project dependencies and tools
WORKDIR /source/src/Events
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Icons project dependencies and tools
WORKDIR /source/src/Icons
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Identity project dependencies and tools
WORKDIR /source/src/Identity
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Notifications project dependencies and tools
WORKDIR /source/src/Notifications
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Sso project dependencies and tools
WORKDIR /source/bitwarden_license/src/Sso
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Restore Scim project dependencies and tools
WORKDIR /source/bitwarden_license/src/Scim
RUN . /tmp/rid.txt && dotnet restore -r $RID

# Copy required project files
WORKDIR /source
COPY server/src/Admin/. ./src/Admin/
COPY server/src/Api/. ./src/Api/
COPY server/src/Events/. ./src/Events/
COPY server/src/Icons/. ./src/Icons/
COPY server/src/Identity/. ./src/Identity/
COPY server/src/Notifications/. ./src/Notifications/
COPY server/bitwarden_license/src/Sso/. ./bitwarden_license/src/Sso/
COPY server/bitwarden_license/src/Scim/. ./bitwarden_license/src/Scim/
COPY server/src/Core/. ./src/Core/
COPY server/src/Infrastructure.Dapper/. ./src/Infrastructure.Dapper/
COPY server/src/Infrastructure.EntityFramework/. ./src/Infrastructure.EntityFramework/
COPY server/src/SharedWeb/. ./src/SharedWeb/
COPY server/util/Migrator/. ./util/Migrator/
COPY server/util/MySqlMigrations/. ./util/MySqlMigrations/
COPY server/util/PostgresMigrations/. ./util/PostgresMigrations/
COPY server/util/SqliteMigrations/. ./util/SqliteMigrations/
COPY server/util/EfShared/. ./util/EfShared/
COPY server/bitwarden_license/src/Commercial.Core/. ./bitwarden_license/src/Commercial.Core/
COPY server/bitwarden_license/src/Commercial.Infrastructure.EntityFramework/. ./bitwarden_license/src/Commercial.Infrastructure.EntityFramework/

# Build Admin app
WORKDIR /source/src/Admin
RUN npm install && npm run build
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Admin --no-restore --no-self-contained -r $RID

# Build Api app
WORKDIR /source/src/Api
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Api --no-restore --no-self-contained -r $RID

# Build Events app
WORKDIR /source/src/Events
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Events --no-restore --no-self-contained -r $RID

# Build Icons app
WORKDIR /source/src/Icons
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Icons --no-restore --no-self-contained -r $RID

# Build Identity app
WORKDIR /source/src/Identity
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Identity --no-restore --no-self-contained -r $RID

# Build Notifications app
WORKDIR /source/src/Notifications
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Notifications --no-restore --no-self-contained -r $RID

# Build Sso app
WORKDIR /source/bitwarden_license/src/Sso
RUN npm install && npm run build
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Sso --no-restore --no-self-contained -r $RID

# Build Scim app
WORKDIR /source/bitwarden_license/src/Scim
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Scim --no-restore --no-self-contained -r $RID

###############################################
#                  App stage                  #
###############################################
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine3.21
ARG TARGETPLATFORM
LABEL com.bitwarden.product="bitwarden" \
      com.bitwarden.project="lite" \
      org.opencontainers.image.description="Bitwarden lite" \
      org.opencontainers.image.source="https://github.com/bitwarden/self-host" \
      org.opencontainers.image.url="https://bitwarden.com" \
      org.opencontainers.image.vendor="Bitwarden Inc."
ENV ASPNETCORE_ENVIRONMENT=Production \
    BW_ENABLE_ADMIN=true \
    BW_ENABLE_API=true \
    BW_ENABLE_EVENTS=false \
    BW_ENABLE_ICONS=true \
    BW_ENABLE_IDENTITY=true \
    BW_ENABLE_NOTIFICATIONS=true \
    BW_ENABLE_SCIM=false \
    BW_ENABLE_SSO=false \
    BW_DB_FILE="/etc/bitwarden/vault.db" \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    globalSettings__selfHosted="true" \
    globalSettings__liteDeployment="true" \
    globalSettings__pushRelayBaseUri="https://push.bitwarden.com" \
    globalSettings__baseServiceUri__internalAdmin="http://localhost:5000" \
    globalSettings__baseServiceUri__internalApi="http://localhost:5001" \
    globalSettings__baseServiceUri__internalEvents="http://localhost:5003" \
    globalSettings__baseServiceUri__internalIcons="http://localhost:5004" \
    globalSettings__baseServiceUri__internalIdentity="http://localhost:5005" \
    globalSettings__baseServiceUri__internalNotifications="http://localhost:5006" \
    globalSettings__baseServiceUri__internalSso="http://localhost:5007" \
    globalSettings__baseServiceUri__internalScim="http://localhost:5002" \
    globalSettings__baseServiceUri__internalVault="http://localhost:8080" \
    globalSettings__identityServer__certificatePassword="default_cert_password" \
    globalSettings__dataProtection__directory="/etc/bitwarden/data-protection" \
    globalSettings__attachment__baseDirectory="/etc/bitwarden/attachments" \
    globalSettings__send__baseDirectory="/etc/bitwarden/attachments/send" \
    globalSettings__licenseDirectory="/etc/bitwarden/licenses" \
    globalSettings__logDirectoryByProject="false" \
    globalSettings__logRollBySizeLimit="1073741824"

EXPOSE 8080 8443

# Add packages
RUN apk add --no-cache \
    curl \
    gcompat \
    icu-libs \
    jq \
    nginx \
    openssl \
    su-exec \
    supervisor \
    tzdata \
    unzip

# Create required directories
RUN mkdir -p /app \
             /etc/bitwarden/attachments/send \
             /etc/bitwarden/data-protection \
             /etc/bitwarden/licenses \
             /etc/bitwarden/logs \
             /etc/nginx/http.d \
             /etc/supervisor \
             /etc/supervisor.d \
             /var/lib/nginx/tmp \
             /var/log/bitwarden \
             /var/log/nginx/logs \
             /var/run/nginx \
    && touch /var/run/nginx/nginx.pid

# Copy all apps from dotnet-build stage
WORKDIR /app
COPY --from=dotnet-build /app ./

# Copy Web files from web-setup stage
COPY --from=web-setup /tmp/build /app/Web

# Set up supervisord
COPY bitwarden-lite/supervisord/*.ini /etc/supervisor.d/
COPY bitwarden-lite/supervisord/supervisord.conf /etc/supervisor/supervisord.conf
RUN rm -f /etc/supervisord.conf

# Set up nginx
COPY bitwarden-lite/nginx/mime.types \
     bitwarden-lite/nginx/nginx.conf \
     bitwarden-lite/nginx/proxy.conf \
     bitwarden-lite/nginx/security-headers.conf \
     bitwarden-lite/nginx/security-headers-ssl.conf \
     /etc/nginx/
COPY --chmod=755 bitwarden-lite/nginx/logrotate.sh /

# Copy configuration templates
COPY bitwarden-lite/hbs/app-id.hbs \
     bitwarden-lite/hbs/config.yaml \
     bitwarden-lite/hbs/nginx-config.hbs \
     /etc/hbs/

# Download and extract hbs tool for generating final configurations
RUN LATEST_VERSION=$(curl --proto "=https" --silent https://api.github.com/repos/bitwarden/Handlebars.conf/git/refs/tags | jq -r 'last(.[].ref)' | sed 's/refs\/tags\///') \
    && case "$TARGETPLATFORM" in \
         "linux/amd64") \
           curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-x64.zip ;; \
         "linux/arm/v7") \
           curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm.zip ;; \
         "linux/arm64") \
           curl --proto "=https" -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm64.zip ;; \
       esac \
    && unzip hbs.zip -d /usr/local/bin && mv /usr/local/bin/hbs* /usr/local/bin/hbs && rm hbs.zip \
    && chmod +x /usr/local/bin/hbs

# Copy entrypoint script and make it executable
COPY --chmod=755 bitwarden-lite/entrypoint.sh /entrypoint.sh

VOLUME ["/etc/bitwarden"]

WORKDIR /app

HEALTHCHECK --interval=30s --timeout=3s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:8080/alive || exit 1

ENTRYPOINT ["/entrypoint.sh"]
