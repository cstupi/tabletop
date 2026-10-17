#!/bin/sh
# Nightly pg_dump: deploy/docker-compose.home.yml's pg-backup service runs it in the postgres image.
#   backup.sh         loop: one dump a day at PG_BACKUP_AT (HH:MM, container time: UTC unless TZ is set)
#   backup.sh once    one dump now, then exit
# Inputs: PGHOST, PGDATABASE, PGUSER, PGPASSWORD; PG_BACKUP_DIR (default /backups), PG_BACKUP_KEEP (default 14),
# PG_BACKUP_AT (default 03:30). Custom format (pg_restore), named tabletop-<UTC timestamp>.dump, written to a .partial file
# and renamed when complete. Only the newest PG_BACKUP_KEEP dumps are kept.
set -eu
: "${PGHOST:?}" "${PGDATABASE:?}" "${PGUSER:?}" "${PGPASSWORD:?}"
dir=${PG_BACKUP_DIR:-/backups} keep=${PG_BACKUP_KEEP:-14} at=${PG_BACKUP_AT:-03:30}

dump() {
  name="tabletop-$(date -u +%Y%m%dT%H%M%SZ).dump"
  if ! pg_dump --format=custom --no-owner --file="$dir/$name.partial"; then
    rm -f "$dir/$name.partial"; echo "backup: pg_dump failed" >&2; return 1
  fi
  mv "$dir/$name.partial" "$dir/$name"
  echo "backup: $dir/$name ($(wc -c < "$dir/$name") bytes)"
  ls -1 "$dir" | grep -E '^tabletop-[0-9]{8}T[0-9]{6}Z\.dump$' | sort -r | tail -n +"$((keep + 1))" | while read -r old; do
    rm -f "$dir/$old" && echo "backup: removed $old"
  done
}

if [ "${1:-}" = once ]; then dump; exit; fi
echo "backup: daily at $at into $dir, keeping $keep"
last=""
while :; do
  now=$(date +%H:%M) today=$(date +%F)
  if [ "$now" = "$at" ] && [ "$last" != "$today" ]; then dump || true; last=$today; fi
  sleep 20
done
