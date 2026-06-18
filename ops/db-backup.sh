#!/bin/sh
# Periodic MySQL backup. Runs inside the db-backup sidecar (mysql:8 image, which
# already has mysqldump). Dumps the database to /backups on a schedule and prunes
# old dumps. The /backups dir is a host bind-mount so dumps survive container loss.
#
# Offsite copy (recommended): the safest version of "Azure as backup" is to ship
# these gzipped dumps to object storage rather than running a second live DB. If
# OFFSITE_UPLOAD_CMD is set, it runs after each dump with $BACKUP_FILE in the env
# (e.g. azcopy/rclone). Leave it empty to keep dumps local only.
set -eu

: "${MYSQL_HOST:=mysql}"
: "${BACKUP_INTERVAL_SECONDS:=86400}"   # default: daily. 432000 = every 5 days.
: "${BACKUP_KEEP:=14}"                   # how many dumps to retain locally

echo "db-backup: interval=${BACKUP_INTERVAL_SECONDS}s keep=${BACKUP_KEEP} db=${MYSQL_DATABASE}"

while true; do
	ts="$(date +%Y%m%d-%H%M%S)"
	BACKUP_FILE="/backups/${MYSQL_DATABASE}-${ts}.sql.gz"

	if mysqldump --single-transaction --quick --no-tablespaces \
		-h "${MYSQL_HOST}" -u"${MYSQL_USER}" -p"${MYSQL_PASSWORD}" \
		"${MYSQL_DATABASE}" | gzip > "${BACKUP_FILE}"; then
		echo "db-backup: wrote ${BACKUP_FILE}"

		if [ -n "${OFFSITE_UPLOAD_CMD:-}" ]; then
			BACKUP_FILE="${BACKUP_FILE}" sh -c "${OFFSITE_UPLOAD_CMD}" \
				&& echo "db-backup: offsite upload ok" \
				|| echo "db-backup: offsite upload FAILED (kept local copy)"
		fi
	else
		echo "db-backup: dump FAILED, removing partial file"
		rm -f "${BACKUP_FILE}"
	fi

	# Prune: keep the newest $BACKUP_KEEP dumps.
	ls -1t /backups/*.sql.gz 2>/dev/null | tail -n "+$((BACKUP_KEEP + 1))" | xargs -r rm -f

	sleep "${BACKUP_INTERVAL_SECONDS}"
done
