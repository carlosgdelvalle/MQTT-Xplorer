#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/MqttExplorer.Avalonia/MqttExplorer.Avalonia.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
PUBLISH_DIR="$ARTIFACTS_DIR/macos-app-publish"
STAGING_DIR="$ARTIFACTS_DIR/macos-app"
APP_NAME="Explorador MQTT"
APP_BUNDLE_NAME="$APP_NAME.app"
APP_BUNDLE_ID="com.cdvpyv.mqttexplorer.avalonia"
EXECUTABLE_NAME="$APP_NAME"
INSTALL_DIR="${INSTALL_DIR:-$HOME/Applications}"
APP_DEST="$INSTALL_DIR/$APP_BUNDLE_NAME"
ICON_SOURCE="$ROOT_DIR/MqttExplorer.Avalonia/Assets/configuration.icns"
ENTITLEMENTS_PLIST="$ROOT_DIR/../res/entitlements.mac.plist"
APP_VERSION="$(
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$APP_PROJECT" | head -n 1
)"

if [[ -z "$APP_VERSION" ]]; then
  echo "Could not determine app version from $APP_PROJECT" >&2
  exit 1
fi

case "$(uname -m)" in
  arm64)
    RUNTIME_ID="osx-arm64"
    ;;
  x86_64)
    RUNTIME_ID="osx-x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

rm -rf "$PUBLISH_DIR" "$STAGING_DIR"

dotnet publish "$APP_PROJECT" -c Release -r "$RUNTIME_ID" -o "$PUBLISH_DIR" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true

APP_STAGING="$STAGING_DIR/$APP_BUNDLE_NAME"
CONTENTS_DIR="$APP_STAGING/Contents"
MACOS_DIR="$CONTENTS_DIR/MacOS"
RESOURCES_DIR="$CONTENTS_DIR/Resources"

mkdir -p "$MACOS_DIR" "$RESOURCES_DIR" "$INSTALL_DIR"
cp "$PUBLISH_DIR/$EXECUTABLE_NAME" "$MACOS_DIR/$EXECUTABLE_NAME"
chmod +x "$MACOS_DIR/$EXECUTABLE_NAME"

if [[ -f "$ICON_SOURCE" ]]; then
  cp "$ICON_SOURCE" "$RESOURCES_DIR/AppIcon.icns"
fi

cat > "$CONTENTS_DIR/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>$APP_NAME</string>
  <key>CFBundleExecutable</key>
  <string>$EXECUTABLE_NAME</string>
  <key>CFBundleIconFile</key>
  <string>AppIcon</string>
  <key>CFBundleIdentifier</key>
  <string>$APP_BUNDLE_ID</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>$APP_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$APP_VERSION</string>
  <key>CFBundleVersion</key>
  <string>$APP_VERSION</string>
  <key>LSMinimumSystemVersion</key>
  <string>13.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
EOF

if [[ -n "${APPLE_SIGN_IDENTITY:-}" ]]; then
  codesign --deep --force --options runtime --timestamp \
    --entitlements "$ENTITLEMENTS_PLIST" \
    --sign "$APPLE_SIGN_IDENTITY" \
    "$APP_STAGING"
  codesign --verify --deep --strict --verbose=2 "$APP_STAGING"
fi

rm -rf "$APP_DEST"
ditto "$APP_STAGING" "$APP_DEST"
touch "$APP_DEST"

echo "Installed: $APP_DEST"
echo "Version: $APP_VERSION"
echo "Launch with: open \"$APP_DEST\""
