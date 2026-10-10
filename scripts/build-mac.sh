#!/usr/bin/env bash
# Builds a self-contained MovieStart.app for Apple Silicon into dist/.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="${MOVIESTART_VERSION:-1.0.0}"
app="$root/dist/MovieStart.app"
out="$root/MovieStart.Desktop/bin/publish-osx-arm64"

rm -rf "$out" "$app"
dotnet publish "$root/MovieStart.Desktop" -c Release -r osx-arm64 --self-contained -p:Version="$version" -o "$out"

mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
# Release builds use the committed appsettings.json only, never personal overrides.
rsync -a --exclude appsettings.Local.json --exclude '*.pdb' "$out/" "$app/Contents/MacOS/"
cp "$root/MovieStart.Desktop/Assets/MovieStart.icns" "$app/Contents/Resources/"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>MovieStart</string>
  <key>CFBundleDisplayName</key><string>MovieStart</string>
  <key>CFBundleIdentifier</key><string>com.moviestart.desktop</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleExecutable</key><string>MovieStart.Desktop</string>
  <key>CFBundleIconFile</key><string>MovieStart</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <!-- macOS asks before an app talks to devices on the local network, such as the Pi. -->
  <key>NSLocalNetworkUsageDescription</key><string>MovieStart connects to the MovieStart agent on your Raspberry Pi to search, download and play movies.</string>
</dict>
</plist>
PLIST

# A stable identity (scripts/create-signing-cert.sh) keeps the Local Network permission
# across rebuilds; the ad-hoc fallback runs too, but macOS forgets the permission each build.
identity="${MOVIESTART_SIGN_IDENTITY:-MovieStart Local}"
if security find-identity -v -p codesigning | grep -q "\"$identity\""; then
  codesign --force --deep --sign "$identity" "$app"
else
  echo "Signing identity \"$identity\" not found, signing ad-hoc (run scripts/create-signing-cert.sh)." >&2
  codesign --force --deep --sign - "$app"
fi
echo "Built $app"
