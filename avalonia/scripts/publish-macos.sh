#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/MqttExplorer.Avalonia/MqttExplorer.Avalonia.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
APP_BINARY_NAME="Explorador MQTT"

publish_target() {
  local runtime="$1"
  local out_dir="$ARTIFACTS_DIR/$runtime"
  dotnet publish "$APP_PROJECT" -c Release -r "$runtime" -o "$out_dir" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true

  if [[ -n "${APPLE_SIGN_IDENTITY:-}" ]]; then
    codesign --deep --force --verify --verbose \
      --sign "$APPLE_SIGN_IDENTITY" \
      "$out_dir/$APP_BINARY_NAME"
  fi
}

publish_target "osx-x64"
publish_target "osx-arm64"

if [[ -n "${APPLE_NOTARY_PROFILE:-}" ]]; then
  ditto -c -k --sequesterRsrc --keepParent "$ARTIFACTS_DIR/osx-arm64" "$ARTIFACTS_DIR/osx-arm64.zip"
  xcrun notarytool submit "$ARTIFACTS_DIR/osx-arm64.zip" --keychain-profile "$APPLE_NOTARY_PROFILE" --wait
fi
