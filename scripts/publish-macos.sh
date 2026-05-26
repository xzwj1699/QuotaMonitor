#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RID="${1:-osx-arm64}"
APP_NAME="Quota Monitor"
EXECUTABLE_NAME="QuotaMonitor"
PROJECT="$ROOT_DIR/src/QuotaMonitor.App.Avalonia/QuotaMonitor.App.Avalonia.csproj"
PUBLISH_DIR="$ROOT_DIR/src/QuotaMonitor.App.Avalonia/bin/Release/net10.0/$RID/publish"
DIST_DIR="$ROOT_DIR/dist"
APP_DIR="$DIST_DIR/$APP_NAME.app"
CONTENTS_DIR="$APP_DIR/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"
ICONSET_DIR="$ROOT_DIR/src/QuotaMonitor.App.Avalonia/obj/macos-icon/QuotaMonitor.iconset"
ICON_FILE="$RESOURCES_DIR/QuotaMonitor.icns"

dotnet publish "$PROJECT" \
  -c Release \
  -r "$RID" \
  --self-contained true

mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$DIST_DIR"
cp -R "$PUBLISH_DIR/." "$MACOS_DIR/"

mkdir -p "$ICONSET_DIR"
if command -v sips >/dev/null 2>&1 && command -v iconutil >/dev/null 2>&1; then
  sips -z 16 16 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_16x16.png" >/dev/null
  sips -z 32 32 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_16x16@2x.png" >/dev/null
  sips -z 32 32 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_32x32.png" >/dev/null
  sips -z 64 64 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_32x32@2x.png" >/dev/null
  sips -z 128 128 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_128x128.png" >/dev/null
  sips -z 256 256 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_128x128@2x.png" >/dev/null
  sips -z 256 256 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_256x256.png" >/dev/null
  sips -z 512 512 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_256x256@2x.png" >/dev/null
  sips -z 512 512 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_512x512.png" >/dev/null
  sips -z 1024 1024 "$ROOT_DIR/assets/quota-monitor.png" --out "$ICONSET_DIR/icon_512x512@2x.png" >/dev/null
  if ! iconutil -c icns "$ICONSET_DIR" -o "$ICON_FILE"; then
    echo "Warning: iconutil could not create the .icns file; the app bundle will still be created."
  fi
fi
cp "$ROOT_DIR/assets/quota-monitor.png" "$RESOURCES_DIR/quota-monitor.png"

cat > "$CONTENTS_DIR/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleExecutable</key>
  <string>$EXECUTABLE_NAME</string>
  <key>CFBundleIconFile</key>
  <string>QuotaMonitor</string>
  <key>CFBundleIdentifier</key>
  <string>dev.quotamonitor.app</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>0.1.0</string>
  <key>CFBundleVersion</key>
  <string>0.1.0</string>
  <key>LSMinimumSystemVersion</key>
  <string>14.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

printf "APPL????" > "$CONTENTS_DIR/PkgInfo"

chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - "$APP_DIR"
fi

echo "Published:"
echo "$ROOT_DIR/src/QuotaMonitor.App.Avalonia/bin/Release/net10.0/$RID/publish"
echo "App bundle:"
echo "$APP_DIR"
