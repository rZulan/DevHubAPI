#!/usr/bin/env bash
set -euo pipefail
umask 0077
release=${1:?Release ID required}
[[ "$release" =~ ^[0-9]{14}-[a-f0-9]{8}$ ]] || exit 2
exec 9>/run/lock/devhub-publish.lock
flock -n 9 || { echo 'Another deployment is running.'; exit 1; }
stage="/opt/devhub/releases/$release"
test -d "$stage"
test -r /etc/devhub/appsettings.Production.json
api_changed=0
client_changed=0
stopped=0
trap 'if (( stopped )); then systemctl start devhub; fi' EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
rollback() {
    status=$?
    trap - ERR
    set +e
    if (( client_changed )); then
        mv /var/www/devhub "$stage/client-failed"
        mv "$stage/client-previous" /var/www/devhub
    fi
    if (( api_changed )); then
        systemctl stop devhub
        mv /opt/devhub/api "$stage/api-failed"
        mv "$stage/api-previous" /opt/devhub/api
    fi
    if (( stopped )); then systemctl start devhub; fi
    echo 'Deployment failed; previous application files restored. Database migrations are NOT automatically reversed.'
    exit "$status"
}
trap rollback ERR
if [[ -f "$stage/api.tar.gz" ]]; then
    mkdir "$stage/api-next"
    tar -xzf "$stage/api.tar.gz" -C "$stage/api-next"
    test -f "$stage/api-next/DevHub.Api"
    ln -s /etc/devhub/appsettings.Production.json "$stage/api-next/appsettings.Production.json"
    chown -R root:devhub "$stage/api-next"
    find "$stage/api-next" -type d -exec chmod 750 {} +
    find "$stage/api-next" -type f -exec chmod 640 {} +
    chmod 750 "$stage/api-next/DevHub.Api"
fi
if [[ -f "$stage/client.tar.gz" ]]; then
    mkdir "$stage/client-next"
    tar -xzf "$stage/client.tar.gz" -C "$stage/client-next"
    test -s "$stage/client-next/index.html"
    chmod -R a+rX "$stage/client-next"
fi
if [[ -d "$stage/api-next" ]]; then
    systemctl stop devhub
    stopped=1
    if [[ -f "$stage/database-backup.tar.gz" ]]; then
        mkdir "$stage/database-backup"
        tar -xzf "$stage/database-backup.tar.gz" -C "$stage/database-backup"
        chmod 700 "$stage/database-backup/DatabaseBackup"
        "$stage/database-backup/DatabaseBackup" "$release"
    fi
    if [[ -f "$stage/efbundle" ]]; then
        chmod 700 "$stage/efbundle"
        (cd "$stage/api-next"; ASPNETCORE_ENVIRONMENT=Production DOTNET_ENVIRONMENT=Production "$stage/efbundle")
    fi
    mv /opt/devhub/api "$stage/api-previous"
    # Record the move before activation so the error handler can restore it.
    api_changed=1
    mv "$stage/api-next" /opt/devhub/api
    runuser -u devhub -- test -x /opt/devhub/api/DevHub.Api
    runuser -u devhub -- test -r /opt/devhub/api/appsettings.Production.json
    systemctl start devhub
    healthy=0
    for attempt in {1..30}; do
        code=$(curl -s -o /dev/null -w '%{http_code}' -H 'X-Forwarded-Proto: https' http://127.0.0.1:5000/api/users/me || true)
        if [[ "$code" == 401 ]]; then healthy=1; break; fi
        sleep 1
    done
    test "$healthy" = 1
fi
if [[ -d "$stage/client-next" ]]; then
    mv /var/www/devhub "$stage/client-previous"
    client_changed=1
    mv "$stage/client-next" /var/www/devhub
fi
curl --fail --silent --output /dev/null https://157.230.58.33/login
echo "Release $release deployed. Previous files retained in $stage."
