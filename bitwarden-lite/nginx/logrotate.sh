#!/bin/sh

while true
do
  [ "$1" = "loop" ] && sleep $((24 * 3600 - (`date +%_H` * 3600 + `date +%_M` * 60 + `date +%_S`)))
  ts=$(date +%Y%m%d_%H%M%S)
  mv /etc/bitwarden/logs/nginx/access.log /etc/bitwarden/logs/nginx/access.$ts.log
  mv /etc/bitwarden/logs/nginx/error.log /etc/bitwarden/logs/nginx/error.$ts.log
  kill -USR1 `cat /tmp/bitwarden/nginx.pid`
  sleep 1
  gzip /etc/bitwarden/logs/nginx/access.$ts.log
  gzip /etc/bitwarden/logs/nginx/error.$ts.log
  find /etc/bitwarden/logs/nginx/ -name "*.gz" -mtime +32 -delete
  [ "$1" != "loop" ] && break
done
