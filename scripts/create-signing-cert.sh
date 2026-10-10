#!/usr/bin/env bash
# Creates a self-signed code signing certificate in the login keychain, once per Mac.
# build-mac.sh signs with it, so macOS keeps privacy permissions (Local Network)
# across rebuilds; an ad-hoc signature changes on every build and loses them.
set -euo pipefail

name="${MOVIESTART_SIGN_IDENTITY:-MovieStart Local}"
keychain="$HOME/Library/Keychains/login.keychain-db"

if security find-identity -v -p codesigning | grep -q "\"$name\""; then
  echo "Signing identity \"$name\" already exists."
  exit 0
fi

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

cat > "$tmp/cert.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = $name
[ext]
basicConstraints = critical, CA:false
keyUsage = critical, digitalSignature
extendedKeyUsage = critical, codeSigning
CNF

openssl req -quiet -x509 -newkey rsa:2048 -nodes -days 3650 \
  -keyout "$tmp/key.pem" -out "$tmp/cert.pem" -config "$tmp/cert.cnf"
# Keychain only reads PKCS#12 with legacy algorithms; the password is throwaway.
openssl pkcs12 -export -legacy -inkey "$tmp/key.pem" -in "$tmp/cert.pem" \
  -name "$name" -out "$tmp/cert.p12" -passout pass:moviestart

security import "$tmp/cert.p12" -k "$keychain" -P moviestart -T /usr/bin/codesign
# Trust the certificate for code signing; macOS asks for the login password.
security add-trusted-cert -r trustRoot -p codeSign -k "$keychain" "$tmp/cert.pem"

echo "Created signing identity \"$name\"."
