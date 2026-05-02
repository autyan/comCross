# Packaging Guide

This guide describes the release packaging model for ComCross.

## 1) Release Policy

Official GitHub Releases should publish installable operating-system packages,
not portable publish archives.

Default release assets:

- Windows:
  - `ComCross-<version>-win-x64.msi`
  - `ComCross-<version>-win-arm64.msi`
- Linux:
  - `comcross_<version>_amd64.deb`
  - `comcross_<version>_arm64.deb`
  - `comcross-<version>-1.x86_64.rpm`
  - `comcross-<version>-1.aarch64.rpm`
  - `ComCross-<version>-linux-x64.AppImage`
  - `ComCross-<version>-linux-arm64.AppImage`
- Verification:
  - `SHA256SUMS`
  - `SHA256SUMS.asc` when signing material is provided

Portable `.tar.gz` and `.zip` publish archives are no longer default release
assets. Users who need bare publish outputs should build them locally from
source.

## 2) Runtime Baseline

Projects target `net8.0`. Development may use any installed SDK that can build
`net8.0`, and release artifacts target the .NET 8 LTS runtime baseline.

Linux DEB/RPM packages are framework-dependent and declare a dependency on
`.NET 8 Runtime`.

Linux AppImage packages are self-contained and do not require a system .NET
runtime. AppImage is the fallback package for users on distributions that are
not covered by DEB/RPM.

Windows MSI packages are self-contained.

## 3) Local Release Verification

The local release entry point is:

```bash
scripts/release/local-verify.sh --version <version>
```

By default this builds Linux x64 and ARM64 publish outputs, then produces:

- DEB/RPM through a local Docker image that carries `fpm`
- AppImage packages from self-contained publish outputs
- `SHA256SUMS`
- optional GPG signature for `SHA256SUMS`

Example:

```bash
scripts/release/local-verify.sh --version 0.5.0
```

With local GPG signing:

```bash
scripts/release/local-verify.sh \
  --version 0.5.0 \
  --gpg-private-key ~/.keys/comcross-release.asc \
  --gpg-key-id <key-id>
```

Local signing material must stay outside the repository. `.release-secrets/` is
ignored for temporary local use, but it must not be used by CI.

## 4) Linux DEB/RPM

Linux packages are built from framework-dependent publish outputs.

The packager uses Docker so local machines and GitHub runners do not need Ruby
or `fpm` installed directly:

```bash
scripts/release/build-publish-output.sh --version 0.5.0
scripts/release/build-linux-packages.sh --version 0.5.0
```

Architecture mapping:

- `linux-x64` -> DEB `amd64`, RPM `x86_64`
- `linux-arm64` -> DEB `arm64`, RPM `aarch64`

Installed layout:

```text
/opt/comcross/
/usr/bin/comcross
/usr/share/applications/comcross.desktop
/usr/share/icons/hicolor/256x256/apps/comcross.png
```

## 5) Linux AppImage

AppImage packages are built from self-contained publish outputs:

```bash
scripts/release/build-appimages.sh --version 0.5.0
```

AppImage packages are intended as a fallback for non-mainstream Linux
distributions. They are not a replacement for distro-native DEB/RPM packages.

## 6) Windows MSI

Windows MSI packages are built on Windows with WiX Toolset v4:

```powershell
dotnet tool install --global wix
scripts/release/build-windows-msi.ps1 -Version 0.5.0
```

The script produces:

```text
artifacts/release/packages/windows/ComCross-<version>-win-x64.msi
artifacts/release/packages/windows/ComCross-<version>-win-arm64.msi
```

MSI signing can be enabled with:

```powershell
scripts/release/build-windows-msi.ps1 `
  -Version 0.5.0 `
  -CertificatePfxPath C:\keys\comcross.pfx `
  -CertificatePassword $env:COMCROSS_WINDOWS_CERT_PASSWORD
```

Windows MSI validation is performed on a Windows machine before the GitHub
Actions release workflow is enabled.

## 7) Signing Inputs

Local verification reads signing material from paths supplied by the developer.
GitHub Actions must read signing material from repository or environment
secrets.

Expected secret names for future CI release work:

- `COMCROSS_GPG_PRIVATE_KEY`
- `COMCROSS_GPG_PASSPHRASE`
- `COMCROSS_GPG_KEY_ID`
- `COMCROSS_WINDOWS_CERT_PFX_BASE64`
- `COMCROSS_WINDOWS_CERT_PASSWORD`

Formal release jobs must fail when required signing material is missing. Local
dry runs may skip signing unless `--require-signing` is passed.

## 8) Plugin Layout

Release packages place built-in plugins under:

```text
plugins/<plugin-id>-<stable-hash>/
```

The package scripts compute the folder from the manifest plugin id and publish
each plugin with its dependencies into that folder.

## 9) Bare Publish Output

Bare publish output is for local development and advanced users. It is not a
default GitHub Release asset.

Framework-dependent example:

```bash
scripts/package-release.sh \
  -c Release \
  -r linux-x64 \
  --no-package
```

Self-contained output is produced by the release pipeline as an intermediate
input for AppImage and MSI packaging.

## 10) Symbols

Public release builds do not include PDBs by default. Use existing publish
scripts with `--include-symbols` for internal debug builds.
