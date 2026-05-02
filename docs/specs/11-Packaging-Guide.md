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

Windows MSI packages are self-contained and installed per user.

## 2.1) Supported Operating Systems

The current official package support baseline is:

| OS | Minimum version | Architecture | Package |
|---|---|---|---|
| Windows | Windows 10 22H2 / Windows 11 22H2 | x64, ARM64 | MSI |
| Ubuntu | 22.04 LTS | x64, ARM64 | DEB, AppImage |
| Debian | 12 | x64, ARM64 | DEB, AppImage |
| Fedora | 40 | x64 | RPM, AppImage |

This is the project compatibility target, not a claim that every desktop
environment, graphics stack, and hardware configuration is fully tested today.

Other Linux distributions may work but are outside the formal support
commitment. AppImage packages are the recommended fallback for these users.

ComCross does not currently provide official macOS packages.

## 3) Runtime Directories

ComCross separates install content, configuration, local data, logs, cache, and
runtime plugins.

Windows:

```text
Main program:
%LocalAppData%\Programs\ComCross\

Configuration:
%AppData%\ComCross\

Local data:
%LocalAppData%\ComCross\

Databases:
%LocalAppData%\ComCross\data\

Logs:
%LocalAppData%\ComCross\logs\

Cache:
%LocalAppData%\ComCross\cache\

Runtime plugins:
%LocalAppData%\ComCross\plugins\
```

Linux:

```text
Main program:
/opt/comcross/

Configuration:
${XDG_CONFIG_HOME:-$HOME/.config}/ComCross/

Local data:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/

Databases:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/data/

Logs:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/logs/

Cache:
${XDG_CACHE_HOME:-$HOME/.cache}/ComCross/

Runtime plugins:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

This is a pre-stable breaking directory relocation. Old configuration,
database, log, cache, plugin session storage, export, and plugin runtime
directories are not kept as compatibility read paths.

## 4) Local Release Verification

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

## 5) Linux DEB/RPM

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

Installed system layout:

```text
/opt/comcross/
/usr/bin/comcross
/usr/share/applications/comcross.desktop
/usr/share/icons/hicolor/256x256/apps/comcross.png
```

Linux package payloads place official plugin packages under:

```text
/opt/comcross/bundled-plugins/
```

At runtime, Core synchronizes official plugin packages into the user-local XDG
data directory and loads plugins from there:

```text
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

## 6) Linux AppImage

AppImage packages are built from self-contained publish outputs:

```bash
scripts/release/build-appimages.sh --version 0.5.0
```

AppImage packages are intended as a fallback for non-mainstream Linux
distributions. They are not a replacement for distro-native DEB/RPM packages.

## 7) Windows MSI

Windows MSI packages are built on Windows with WiX Toolset v7. The repository
pins the WiX version through a .NET tool manifest so local and CI builds use the
same toolchain.

```powershell
dotnet tool restore
scripts/release/build-windows-msi.ps1 -Version 0.5.0
```

WiX v7 requires explicit OSMF EULA acceptance. Local and CI builds must pass
the WiX v7 EULA acceptance flag when invoking `wix build`.

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

Windows MSI packages install per user and must not require administrator
elevation for normal install, update, or uninstall.

Official plugin packages are bundled with the installer as seed content:

```text
%LocalAppData%\Programs\ComCross\bundled-plugins\
```

At runtime, Core synchronizes official plugin packages into the user-local
plugin directory and loads plugins from there:

```text
%LocalAppData%\ComCross\plugins\
```

## 8) Signing Inputs

Local verification reads signing material from paths supplied by the developer.
GitHub Actions must read signing material from repository or environment
secrets.

Expected secret names for future CI release work:

- `COMCROSS_GPG_PRIVATE_KEY`
- `COMCROSS_GPG_PASSPHRASE`
- `COMCROSS_GPG_KEY_ID`
- `COMCROSS_PLUGIN_SIGNING_KEY_PEM`
- `COMCROSS_PLUGIN_SIGNING_KEY_ID`
- `COMCROSS_WINDOWS_CERT_PFX_BASE64`
- `COMCROSS_WINDOWS_CERT_PASSWORD`

Formal release jobs must fail when required signing material is missing. Local
dry runs may skip signing unless `--require-signing` is passed.

## 8.1) GitHub Actions Release Workflow

The automated release workflow is:

```text
.github/workflows/release.yml
```

It is manually triggered so pre-release validation and final release publishing
remain explicit release-manager actions:

```bash
gh workflow run release.yml \
  -f version=0.5.0-rc.1 \
  -f prerelease=true \
  -f draft=true \
  -f require_signing=false
```

For formal releases, use `require_signing=true` after the signing secrets are
configured.

The workflow builds Linux packages on Ubuntu, Windows MSI packages on Windows,
regenerates a unified `SHA256SUMS` file across all package assets, optionally
signs the checksum file, and creates the GitHub Release or Pre-release.

## 9) Plugin Layout

Official plugins are normal plugin packages. They have no special runtime
treatment. They are released with the main application package for now, but the
runtime loads them from the same plugin directory that future third-party
plugins will use.

Runtime plugin packages use isolated plugin directories:

```text
<runtime-plugin-directory>/<plugin-id>-<stable-hash>/
```

The package scripts compute the folder from the manifest plugin id and publish
each plugin with its dependencies into that folder.

Runtime plugin roots:

```text
Windows:
%LocalAppData%\ComCross\plugins\

Linux:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

Install packages carry official plugins as bundled seed content:

```text
Windows:
%LocalAppData%\Programs\ComCross\bundled-plugins\

Linux:
/opt/comcross/bundled-plugins/
```

Core owns synchronizing bundled official plugins into the runtime plugin root
before plugin discovery.

The detailed decision record is maintained in:

```text
docs/release/windows-linux-packaging-decisions.md
```

## 10) Bare Publish Output

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

## 11) Symbols

Public release builds do not include PDBs by default. Use existing publish
scripts with `--include-symbols` for internal debug builds.
