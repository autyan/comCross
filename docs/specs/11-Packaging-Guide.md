# Packaging Guide

This guide describes how to produce release artifacts for GitHub.

## 1) Publish

Use the packaging script to generate platform-specific outputs in one pass:

```
scripts/package-release.sh -c Release -r linux-x64,linux-arm64,win-x64,win-arm64
```

Outputs are placed under:

- Framework-dependent: `artifacts/framework-dependent/ComCross-<rid>-Release/`
- Self-contained: `artifacts/self-contained/ComCross-<rid>-Release/`
- Archives: `artifacts/packages/<track>/ComCross-<rid>-Release.(zip|tar.gz)`

The `ComCross.PluginHost` binary is published into the same folder for plugin isolation.

## 2) Runtime Baseline

Development uses the .NET 10 SDK, but release artifacts should target a lower
runtime baseline for compatibility. Current baseline: .NET 8 LTS. Packaging for
DEB/RPM should assume distro baselines that ship or support .NET 8 (for example
Ubuntu 22.04, Debian 12, Fedora 39).

If you do not bundle the runtime (framework-dependent publish), ensure the
package declares runtime dependencies so installation fails when the runtime is
too old.

We commit to keeping the runtime baseline stable for users. The baseline will
only change when strictly necessary, and any breaking changes will be announced
ahead of time.

## 3) Windows (MSI)

Recommended toolchain: WiX Toolset v4.

1. Install WiX.
2. Point the MSI project to the publish output folder.
3. Produce `ComCross-<version>.msi`.

## 4) Linux (DEB/RPM)

Recommended toolchain: fpm.

Helper script:

```
scripts/package-linux.sh -v <version>
```

Example (DEB):

```
fpm -s dir -t deb -n comcross -v <version> \
  -C artifacts/framework-dependent/ComCross-linux-x64-Release \
  --prefix /opt/comcross \
  -d dotnet-runtime-8.0
```

Example (RPM):

```
fpm -s dir -t rpm -n comcross -v <version> \
  -C artifacts/framework-dependent/ComCross-linux-x64-Release \
  --prefix /opt/comcross \
  -d dotnet-runtime-8.0
```

## 5) Release Artifacts

Upload to GitHub Releases with consistent naming:

- `ComCross-linux-x64-<version>.tar.gz`
- `ComCross-linux-arm64-<version>.tar.gz`
- `ComCross-win-x64-<version>.zip`
- `ComCross-win-arm64-<version>.zip`
- `ComCross-<version>.msi`
- `comcross_<version>_amd64.deb`
- `comcross-<version>.x86_64.rpm`

## 6) Symbols

Public release builds do not include PDBs by default. Use
`scripts/package-release.sh --include-symbols` if you need symbols for internal
debug builds.
