#!/bin/sh

while true
do
  [ "$1" = "loop" ] && sleep $((24 * 3600 - (`date +%_H` * 3600 + `date +%_M` * 60 + `date +%_S`)))
  ts=$(date +%Y%m%d_%H%M%S)
  mv /var/log/bitwarden/nginx/access.log /var/log/bitwarden/nginx/access.$ts.log
  mv /var/log/bitwarden/nginx/error.log /var/log/bitwarden/nginx/error.$ts.log
  kill -USR1 `cat /tmp/bitwarden/nginx.pid`
  sleep 1
  gzip /var/log/bitwarden/nginx/access.$ts.log
  gzip /var/log/bitwarden/nginx/error.$ts.log
  find /var/log/bitwarden/nginx/ -name "*.gz" -mtime +32 -delete
  [ "$1" != "loop" ] && break
done
