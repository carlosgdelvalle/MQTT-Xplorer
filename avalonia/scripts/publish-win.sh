#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
APP_PROJECT="$ROOT_DIR/MqttExplorer.Avalonia/MqttExplorer.Avalonia.csproj"
OUT_DIR="$ROOT_DIR/artifacts/win-x64"

dotnet publish "$APP_PROJECT" -c Release -r win-x64 -o "$OUT_DIR" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true

if [[ -n "${SIGNTOOL_PATH:-}" && -n "${WINDOWS_CERT_THUMBPRINT:-}" ]]; then
  "$SIGNTOOL_PATH" sign /fd SHA256 /sha1 "$WINDOWS_CERT_THUMBPRINT" "$OUT_DIR/MqttExplorer.Avalonia.exe"
fi
