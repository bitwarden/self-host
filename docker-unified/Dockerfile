# syntax = docker/dockerfile:1.11
###############################################
#                 Build stage                 #
###############################################
FROM --platform=$BUILDPLATFORM debian AS web-setup

# Add packages
RUN apt-get update && apt-get install -y \
    curl \
    jq \
    unzip \
    git \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /tmp

# Grab last tag/release of the 'web' client
RUN git ls-remote --tags https://github.com/bitwarden/clients.git | grep refs/tags/web | cut -d/ -f3 | sort -Vr | head -1 > tag.txt

RUN cat tag.txt

# Extract the version of the 'web' client
RUN cat tag.txt | grep -o -E "[0-9]{4}\.[0-9]{1,2}\.[0-9]+" > version.txt

# Download the built release artifact for the 'web' client
RUN TAG=$(cat tag.txt) \
  && VERSION=$(cat version.txt) \
  && curl -L https://github.com/bitwarden/clients/releases/download/$TAG/web-$VERSION-selfhosted-COMMERCIAL.zip -O

# Unzip the 'web' client to /tmp/build
RUN VERSION=$(cat version.txt) \
  && unzip web-$VERSION-selfhosted-COMMERCIAL.zip

###############################################
#                 Build stage                 #
###############################################
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS dotnet-build

# Docker buildx supplies the value for this arg
ARG TARGETPLATFORM

# Determine proper runtime value for .NET
# We put the value in a file to be read by later layers.
RUN if [ "$TARGETPLATFORM" = "linux/amd64" ]; then \
      RID=linux-x64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm64" ]; then \
      RID=linux-arm64 ; \
    elif [ "$TARGETPLATFORM" = "linux/arm/v7" ]; then \
      RID=linux-arm ; \
    fi \
    && echo "RID=$RID" > /tmp/rid.txt

# Add packages
RUN apt-get update && apt-get install -y \
    npm \
    && rm -rf /var/lib/apt/lists/*

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
COPY server/.git/. ./.git/

# Build Admin app
WORKDIR /source/src/Admin
RUN npm install
RUN npm run build
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
RUN npm install
RUN npm run build
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Sso --no-restore --no-self-contained -r $RID

# Build Scim app
WORKDIR /source/bitwarden_license/src/Scim
RUN . /tmp/rid.txt && dotnet publish -c release -o /app/Scim --no-restore --no-self-contained -r $RID

###############################################
#                  App stage                  #
###############################################
FROM mcr.microsoft.com/dotnet/aspnet:8.0
ARG TARGETPLATFORM
LABEL com.bitwarden.product="bitwarden"
LABEL com.bitwarden.project="unified"
ENV ASPNETCORE_ENVIRONMENT=Production
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
ENV globalSettings__unifiedDeployment="true"
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
RUN apt-get update && apt-get install -y \
    curl \
    jq \
    nginx \
    openssl \
    supervisor \
    tzdata \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Create required directories
RUN mkdir -p /etc/bitwarden/attachments/send
RUN mkdir -p /etc/bitwarden/data-protection
RUN mkdir -p /etc/bitwarden/licenses
RUN mkdir -p /etc/bitwarden/logs
RUN mkdir -p /etc/supervisor
RUN mkdir -p /etc/supervisor.d
RUN mkdir -p /var/log/bitwarden
RUN mkdir -p /var/log/nginx/logs
RUN mkdir -p /etc/nginx/http.d
RUN mkdir -p /var/run/nginx
RUN mkdir -p /var/lib/nginx/tmp
RUN touch /var/run/nginx/nginx.pid
RUN mkdir -p /app

# Copy all apps from dotnet-build stage
WORKDIR /app
COPY --from=dotnet-build /app ./

# Copy Web files from web-setup stage
COPY --from=web-setup /tmp/build /app/Web

# Set up supervisord
COPY docker-unified/supervisord/*.ini /etc/supervisor.d/
COPY docker-unified/supervisord/supervisord.conf /etc/supervisor/supervisord.conf
RUN rm -f /etc/supervisord.conf

# Set up nginx
COPY docker-unified/nginx/nginx.conf /etc/nginx
COPY docker-unified/nginx/proxy.conf /etc/nginx
COPY docker-unified/nginx/mime.types /etc/nginx
COPY docker-unified/nginx/security-headers.conf /etc/nginx
COPY docker-unified/nginx/security-headers-ssl.conf /etc/nginx
COPY docker-unified/nginx/logrotate.sh /
RUN chmod +x /logrotate.sh

# Copy configuration templates
COPY docker-unified/hbs/nginx-config.hbs /etc/hbs/
COPY docker-unified/hbs/app-id.hbs /etc/hbs/
COPY docker-unified/hbs/config.yaml /etc/hbs/

# Download hbs tool for generating final configurations
RUN echo "$(curl --silent https://api.github.com/repos/bitwarden/Handlebars.conf/git/refs/tags | jq -r 'last(.[].ref)' | sed 's/refs\/tags\///')"  > /tmp/latest.txt
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/amd64" ] ; then curl -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-x64.zip; fi
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/arm/v7" ] ; then curl -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm.zip; fi
RUN LATEST_VERSION=$(cat /tmp/latest.txt) && if [ "$TARGETPLATFORM" = "linux/arm64" ] ; then curl -L --output hbs.zip https://github.com/bitwarden/Handlebars.conf/releases/download/$LATEST_VERSION/hbs_linux-arm64.zip; fi

# Extract hbs
RUN unzip hbs.zip -d /usr/local/bin && mv /usr/local/bin/hbs* /usr/local/bin/hbs && rm hbs.zip
RUN chmod +x /usr/local/bin/hbs

# Copy entrypoint script and make it executable
COPY docker-unified/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

VOLUME ["/etc/bitwarden"]

WORKDIR /app
ENTRYPOINT ["/entrypoint.sh"]
