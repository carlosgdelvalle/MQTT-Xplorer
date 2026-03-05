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
- Optional signing env var:
  - `APPLE_SIGN_IDENTITY` (codesign identity)
- Optional notarization env var:
  - `APPLE_NOTARY_PROFILE` (keychain profile name for `xcrun notarytool`)

## Output

- Build artifacts are created under `avalonia/artifacts/`.
