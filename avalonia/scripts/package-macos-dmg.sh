#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/MqttExplorer.Avalonia/MqttExplorer.Avalonia.csproj"
ARTIFACTS_DIR="$ROOT_DIR/artifacts"
INSTALL_SCRIPT="$ROOT_DIR/scripts/install-macos-app.sh"
APP_NAME="Explorador MQTT"
APP_BUNDLE_NAME="$APP_NAME.app"
INSTALL_DIR="${INSTALL_DIR:-$HOME/Applications}"
APP_PATH="$INSTALL_DIR/$APP_BUNDLE_NAME"
APP_VERSION="$(
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$APP_PROJECT" | head -n 1
)"

case "$(uname -m)" in
  arm64)
    ARCH_LABEL="arm64"
    ;;
  x86_64)
    ARCH_LABEL="x64"
    ;;
  *)
    echo "Unsupported macOS architecture: $(uname -m)" >&2
    exit 1
    ;;
esac

if [[ -z "$APP_VERSION" ]]; then
  echo "Could not determine app version from $APP_PROJECT" >&2
  exit 1
fi

notarytool_args() {
  if [[ -n "${APPLE_NOTARY_PROFILE:-}" ]]; then
    printf -- '--keychain-profile\0%s\0' "$APPLE_NOTARY_PROFILE"
    return
  fi

  if [[ -n "${APPLE_ID:-}" && -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" && -n "${APPLE_TEAM_ID:-}" ]]; then
    printf -- '--apple-id\0%s\0--password\0%s\0--team-id\0%s\0' \
      "$APPLE_ID" "$APPLE_APP_SPECIFIC_PASSWORD" "$APPLE_TEAM_ID"
    return
  fi
}

"$INSTALL_SCRIPT"

if [[ ! -d "$APP_PATH" ]]; then
  echo "Expected app bundle not found at $APP_PATH" >&2
  exit 1
fi

DMG_STAGING_DIR="$ARTIFACTS_DIR/dmg-staging"
DMG_BASENAME="Explorador-MQTT-${APP_VERSION}-macos-${ARCH_LABEL}"
DMG_PATH="$ARTIFACTS_DIR/$DMG_BASENAME.dmg"
TEMP_DMG_PATH="$ARTIFACTS_DIR/$DMG_BASENAME-tmp.dmg"

rm -rf "$DMG_STAGING_DIR" "$DMG_PATH" "$TEMP_DMG_PATH"
mkdir -p "$DMG_STAGING_DIR"
ditto "$APP_PATH" "$DMG_STAGING_DIR/$APP_BUNDLE_NAME"
ln -s /Applications "$DMG_STAGING_DIR/Applications"

hdiutil create -volname "$APP_NAME" \
  -srcfolder "$DMG_STAGING_DIR" \
  -ov -format UDZO \
  "$DMG_PATH"

if [[ -n "${APPLE_SIGN_IDENTITY:-}" ]]; then
  codesign --force --timestamp --sign "$APPLE_SIGN_IDENTITY" "$DMG_PATH"
  codesign --verify --verbose=2 "$DMG_PATH"
fi

NOTARY_ARGS=()
while IFS= read -r -d '' arg; do
  NOTARY_ARGS+=("$arg")
done < <(notarytool_args || true)

if (( ${#NOTARY_ARGS[@]} > 0 )); then
  xcrun notarytool submit "$DMG_PATH" "${NOTARY_ARGS[@]}" --wait
  xcrun stapler staple "$APP_PATH"
  xcrun stapler staple "$DMG_PATH"
  xcrun stapler validate "$APP_PATH"
  xcrun stapler validate "$DMG_PATH"
fi

rm -rf "$DMG_STAGING_DIR" "$TEMP_DMG_PATH"

echo "Created DMG: $DMG_PATH"
if (( ${#NOTARY_ARGS[@]} > 0 )); then
  echo "Notarization: completed and stapled"
else
  echo "Notarization: skipped (set APPLE_NOTARY_PROFILE or APPLE_ID / APPLE_APP_SPECIFIC_PASSWORD / APPLE_TEAM_ID)"
fi
