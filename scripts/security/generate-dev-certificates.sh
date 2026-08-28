#!/usr/bin/env bash
#
# Regenerates the local-development TLS material under src/API/Certificates and
# src/WebSite/Certificates.
#
# Why this script exists: the certificate those two projects served
# (`https:certificate:file` = `Certificates/certificate.pfx` in both appsettings.json) was generated
# in September 2022 with a one-year lifetime and expired on 2023-09-14. Every local client then
# failed its TLS handshake — the desktop client reporting only "The SSL connection could not be
# established" — and the fix was rediscovered by hand each time. A one-year certificate with no way
# to reissue it is a trap that re-arms itself, so reissuing is now one command with a ten-year
# lifetime.
#
# What it deliberately does NOT change:
#
#  * The file names. `Tools.Security.CommittedCertificates` refuses to boot a host configured with
#    these names (Track 7 finding NR-2026-003) because their private keys are public the moment they
#    are committed. Renaming them would silently disarm that guard. Regenerating in place keeps it
#    armed: these files stay Debug-only material.
#  * The password. It stays the placeholder `pass` that appsettings.json ships, which is on the same
#    guard's refusal list for the same reason. This material is not a secret and must not start
#    looking like one.
#
# So the output of this script is still, by design, unfit for any deployment. It exists to make
# `make gui` work against a locally-run API, nothing more. Real hosts follow docs/security/SECRETS.md.
#
# Usage:
#   ./scripts/security/generate-dev-certificates.sh [--days <n>] [--password <pw>]

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

DAYS=3650
PASSWORD="pass"

while [[ $# -gt 0 ]]; do
    case "$1" in
        --days)     DAYS="$2"; shift 2 ;;
        --password) PASSWORD="$2"; shift 2 ;;
        -h|--help)  sed -n '2,30p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "Unknown argument: $1" >&2; exit 2 ;;
    esac
done

command -v openssl >/dev/null 2>&1 || { echo "openssl is required but not on PATH" >&2; exit 2; }

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

# The subjectAltName entries are the point of the exercise, not decoration. The desktop client's
# configured server URL is https://127.0.0.1:5443/, and hostname validation matches the IP against
# an iPAddress SAN — a certificate carrying only CN=localhost fails against that URL even when it is
# otherwise trusted, which is exactly the sort of "fixed it but it still does not work" that sends
# someone back to disabling validation altogether.
echo "Generating a ${DAYS}-day self-signed development certificate..."

openssl req -x509 -newkey rsa:2048 -sha256 -days "$DAYS" -nodes \
    -keyout "$WORK/key.pem" -out "$WORK/certificate.pem" \
    -subj "/C=BR/ST=Development/L=Development/O=NetRisk/OU=Local Development/CN=localhost" \
    -addext "subjectAltName=DNS:localhost,DNS:*.localhost,IP:127.0.0.1,IP:::1" \
    -addext "basicConstraints=critical,CA:TRUE" \
    -addext "keyUsage=critical,digitalSignature,keyEncipherment,keyCertSign" \
    -addext "extendedKeyUsage=serverAuth" \
    2>/dev/null

openssl pkcs12 -export \
    -out "$WORK/certificate.pfx" \
    -inkey "$WORK/key.pem" \
    -in "$WORK/certificate.pem" \
    -name "NetRisk local development" \
    -passout "pass:$PASSWORD"

for project in API WebSite; do
    target="$ROOT/src/$project/Certificates"
    [[ -d "$target" ]] || { echo "Missing directory: $target" >&2; exit 1; }

    cp "$WORK/certificate.pem" "$target/certificate.pem"
    cp "$WORK/key.pem"         "$target/key.pem"
    cp "$WORK/certificate.pfx" "$target/certificate.pfx"

    echo "  wrote src/$project/Certificates/{certificate.pem,key.pem,certificate.pfx}"
done

echo
openssl x509 -in "$WORK/certificate.pem" -noout -subject -dates -ext subjectAltName
echo
echo "Done. Restart the API (make api) so Kestrel picks up the new certificate."
