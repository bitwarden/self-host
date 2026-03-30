# syntax = docker/dockerfile:1.21
ARG SERVER_TAG=dev
ARG WEB_TAG=dev

###############################################
#              Web app stage                  #
###############################################
FROM ghcr.io/bitwarden/web:${WEB_TAG} AS web-app

###############################################
#              Server app stages              #
###############################################
FROM ghcr.io/bitwarden/admin:${SERVER_TAG} AS admin-app
FROM ghcr.io/bitwarden/api:${SERVER_TAG} AS api-app
FROM ghcr.io/bitwarden/events:${SERVER_TAG} AS events-app
FROM ghcr.io/bitwarden/icons:${SERVER_TAG} AS icons-app
FROM ghcr.io/bitwarden/identity:${SERVER_TAG} AS identity-app
FROM ghcr.io/bitwarden/notifications:${SERVER_TAG} AS notifications-app
FROM ghcr.io/bitwarden/sso:${SERVER_TAG} AS sso-app
FROM ghcr.io/bitwarden/scim:${SERVER_TAG} AS scim-app

###############################################
#                  App stage                  #
###############################################
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine3.21
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
    curl \
    jq \
    nginx \
    openssl \
    supervisor \
    tzdata \
    unzip \
    su-exec \
    icu-libs \
    gcompat

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

# Copy compiled apps from server images
WORKDIR /app
COPY --from=admin-app /app /app/Admin
COPY --from=api-app /app /app/Api
COPY --from=events-app /app /app/Events
COPY --from=icons-app /app /app/Icons
COPY --from=identity-app /app /app/Identity
COPY --from=notifications-app /app /app/Notifications
COPY --from=sso-app /app /app/Sso
COPY --from=scim-app /app /app/Scim

# Copy Web files from web-app stage
COPY --from=web-app /app /app/Web

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

VOLUME ["/etc/bitwarden"]

WORKDIR /app
ENTRYPOINT ["/entrypoint.sh"]
