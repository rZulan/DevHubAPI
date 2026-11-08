#!/usr/bin/env bash
set -euo pipefail
umask 022
export PATH="$PATH:/snap/bin:/opt/certbot/bin"

release=${1:?Missing release ID}
admin_id=${2:?Missing admin setup value}
shift 2
[[ "$release" =~ ^[0-9]{14}-[a-f0-9]{8}$ ]]
[[ "$admin_id" == - || "$admin_id" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
(( $# > 0 ))
for site; do
    [[ "$site" == admin || "$site" == docs ]] || exit 2
done

exec 9>/run/lock/devhub-publish.lock
flock -n 9 || { echo 'Another deployment is running.'; exit 1; }
stage="/opt/devhub/releases/subsites-$release"
web="/var/www/devhub-subsites"
dropin=/etc/systemd/system/devhub.service.d/60-zevont-admin.conf
test -d "$stage"
install -d -m 755 "$web/acme"

if [[ "$admin_id" != - ]]; then
    cat > "$stage/admin-service.conf" <<EOF
[Service]
Environment=PlatformAdmin__Origin=https://admin.dev-hub.site
Environment=PlatformAdmin__UserIds__0=$admin_id
EOF
    if [[ -e "$dropin" ]] && ! cmp -s "$stage/admin-service.conf" "$dropin"; then
        echo "An existing platform admin configuration differs. Review $dropin before publishing."
        exit 1
    fi
fi

publish_site() (
    site=$1
    host="$site.dev-hub.site"
    root="$web/$site"
    conf="/etc/nginx/conf.d/zevont-$site.conf"
    certificate="/etc/letsencrypt/live/zevont-$site"
    test -s "$stage/$site.tar.gz"
    if [[ -e "$conf" ]]; then
        grep -qx '# Managed by Zevont subsite publishing' "$conf" ||
            { echo "Refusing to overwrite $conf"; exit 1; }
        cp -p "$conf" "$stage/$site-nginx.previous"
    elif nginx -T 2>&1 | awk -v host="$host" '
        $1 == "server_name" {
            for (i = 2; i <= NF; i++) {
                name = $i
                sub(/;$/, "", name)
                if (name == host) found = 1
                if ($i ~ /;$/) break
            }
        }
        END {exit !found}
    '; then
        echo "Nginx already has a server block for $host. Review it before publishing."
        exit 1
    fi
    if [[ -e "$root/current" && ! -L "$root/current" ]]; then
        echo "Refusing to replace $root/current because it is not a release symlink."
        exit 1
    fi
    previous=$(readlink "$root/current" || true)
    install -d -m 755 "$root/$release"
    tar -xzf "$stage/$site.tar.gz" --no-same-owner --no-same-permissions -C "$root/$release"
    chmod -R a+rX "$root/$release"
    test -s "$root/$release/index.html"

    rollback() {
        code=$?
        trap - ERR
        set +e
        if [[ -f "$stage/$site-nginx.previous" ]]; then
            cp -p "$stage/$site-nginx.previous" "$conf"
        else
            rm -f "$conf"
        fi
        if [[ -n "$previous" ]]; then
            ln -sfn "$previous" "$root/current"
        else
            rm -f "$root/current"
        fi
        nginx -t && systemctl reload nginx
        exit "$code"
    }
    trap rollback ERR

    check_https() {
        local path=$1 status
        if curl --disable --noproxy '*' --fail --silent --show-error \
            --retry 6 --retry-all-errors --retry-delay 1 --retry-max-time 15 --max-time 5 \
            --resolve "$host:443:127.0.0.1" "https://$host$path" -o /dev/null; then
            return 0
        else
            status=$?
        fi
        if [[ "$status" == 60 ]] && command -v openssl >/dev/null; then
            echo "TLS certificate configured for $host:" >&2
            openssl x509 -in "$certificate/fullchain.pem" -noout -subject -issuer -dates -ext subjectAltName >&2 || true
            echo "TLS certificate served by Nginx for $host:" >&2
            timeout 5 openssl s_client -connect 127.0.0.1:443 -servername "$host" </dev/null 2>/dev/null |
                openssl x509 -noout -subject -issuer -dates -ext subjectAltName >&2 || true
        fi
        return "$status"
    }

    write_http() {
        printf '%s\n' '# Managed by Zevont subsite publishing'
        cat <<EOF
server {
    listen 80;
    server_name $host;
    location ^~ /.well-known/acme-challenge/ { root $web/acme; }
    location / { $1 }
}
EOF
    }
    if [[ ! -s "$certificate/fullchain.pem" || ! -s "$certificate/privkey.pem" ]]; then
        write_http 'return 404;' > "$conf"
        nginx -t
        systemctl reload nginx
        certbot certonly --webroot -w "$web/acme" --non-interactive --keep-until-expiring \
            --cert-name "zevont-$site" -d "$host" \
            --deploy-hook 'nginx -t && systemctl reload nginx'
    fi

    {
        write_http "return 301 https://$host\$request_uri;"
        cat <<EOF
server {
    listen 443 ssl;
    server_name $host;
    ssl_certificate $certificate/fullchain.pem;
    ssl_certificate_key $certificate/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    root $root/current;
    index index.html;
    add_header X-Content-Type-Options nosniff always;
    add_header Referrer-Policy no-referrer always;
    add_header Strict-Transport-Security "max-age=31536000" always;
    add_header Content-Security-Policy "default-src 'self'; frame-ancestors 'none'; object-src 'none'; base-uri 'none'" always;
    location / { try_files \$uri \$uri/ =404; }
EOF
        if [[ "$site" == admin ]]; then
            cat <<'NGINX'
    client_max_body_size 32k;
    add_header X-Robots-Tag "noindex, nofollow" always;
    location ~ ^/api/(admin/overview|auth/(login|refresh|logout))$ {
        proxy_pass http://127.0.0.1:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $remote_addr;
    }
NGINX
        fi
        echo '}'
    } > "$stage/$site-nginx.next"

    ln -s "$root/$release" "$root/.current-$release"
    mv -Tf "$root/.current-$release" "$root/current"
    install -m 644 "$stage/$site-nginx.next" "$conf"
    nginx -t
    systemctl reload nginx
    check_https /
    if [[ "$site" == docs ]]; then
        for page in workshop erd; do
            check_https "/$page/"
        done
    fi
    trap - ERR
    echo "Published $host"
)

for site; do
    if [[ "$site" == admin && "$admin_id" == - && ! -s "$dropin" ]]; then
        echo 'Run the first admin publish with -Configure.'
        exit 1
    fi
    publish_site "$site"
done

if [[ "$admin_id" != - && ! -e "$dropin" ]]; then
    install -D -m 600 "$stage/admin-service.conf" "$dropin"
    systemctl daemon-reload
fi
