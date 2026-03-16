# Avalonia Packaging (MVP)

## Windows x64

- Build + publish:
  - `./scripts/publish-win.sh`
- Optional code-signing env vars:
  - `SIGNTOOL_PATH` (path to `signtool.exe`)
  - `WINDOWS_CERT_THUMBPRINT` (certificate thumbprint in cert store)

## macOS (x64 + arm64)

- Build + publish:
  - `./scripts/publish-macos.sh`
- Build an installable local `.app` and copy it into `~/Applications`:
  - `./scripts/install-macos-app.sh`
- Update the installed local app after future code changes:
  - `./scripts/update-macos-app.sh`
- Export a versioned DMG for sharing/installing on another Mac:
  - `./scripts/package-macos-dmg.sh`
- Optional signing env var:
  - `APPLE_SIGN_IDENTITY` (Developer ID Application identity for `codesign`)
- Optional notarization env vars:
  - `APPLE_NOTARY_PROFILE` (keychain profile name for `xcrun notarytool`)
  - or `APPLE_ID`, `APPLE_APP_SPECIFIC_PASSWORD`, and `APPLE_TEAM_ID`
- Notarization-ready release flow:
  - `APPLE_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" APPLE_NOTARY_PROFILE="your-profile" ./scripts/package-macos-dmg.sh`
  - This signs the `.app`, signs the DMG, submits it to Apple, waits for approval, and staples the ticket to both artifacts.

## Output

- Build artifacts are created under `avalonia/artifacts/`.
- Published executable name: `Explorador MQTT` (`Explorador MQTT.exe` on Windows).
- macOS DMGs are versioned from `MqttExplorer.Avalonia.csproj`, for example `Explorador-MQTT-1.0.0-macos-arm64.dmg`.
