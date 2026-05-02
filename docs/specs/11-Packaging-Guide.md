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

`ComCross.PluginHost`, `ComCross.SessionHost`, and `ComCross.ExtensionHost` are published into the same folder so process-isolated plugin flows can start without relying on development paths.

## 2) Runtime Baseline

Projects target `net8.0`. Development may use any installed SDK that can build
`net8.0`, and release artifacts target the .NET 8 LTS runtime baseline.
Packaging for DEB/RPM should assume distro baselines that ship or support .NET 8
(for example Ubuntu 22.04, Debian 12, Fedora 39).

If you do not bundle the runtime (framework-dependent publish), ensure the
package declares runtime dependencies so installation fails when the runtime is
too old.

We commit to keeping the runtime baseline stable for users. The baseline will
only change when strictly necessary, and any breaking changes will be announced
ahead of time.

## 3) Runtime Directories

ComCross separates install content, configuration, local data, logs, cache, and
runtime plugins.

Windows:

```text
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

## 4) Windows (MSI)

Recommended toolchain: WiX Toolset v4.

1. Install WiX.
2. Point the MSI project to the publish output folder.
3. Produce `ComCross-<version>.msi`.

## 5) Linux (DEB/RPM)

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

## 6) Plugin Layout

Official plugins are normal plugin packages. Build and release outputs carry
them as bundled seed content under:

```
bundled-plugins/<plugin-id>-<stable-hash>/
```

The package script computes the folder from the manifest plugin id and publishes each plugin with its dependencies into that folder.

At runtime, Core synchronizes bundled official plugin packages into the
user-local runtime plugin root and discovers plugins from there:

```
Windows:
%LocalAppData%\ComCross\plugins\

Linux:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

This is a pre-stable breaking directory relocation. The old
`AppContext.BaseDirectory/plugins` runtime layout is not kept as a compatibility
read path.

## 7) Release Artifacts

Upload to GitHub Releases with consistent naming:

- `ComCross-linux-x64-<version>.tar.gz`
- `ComCross-linux-arm64-<version>.tar.gz`
- `ComCross-win-x64-<version>.zip`
- `ComCross-win-arm64-<version>.zip`
- `ComCross-<version>.msi`
- `comcross_<version>_amd64.deb`
- `comcross-<version>.x86_64.rpm`

## 8) Symbols

Public release builds do not include PDBs by default. Use
`scripts/package-release.sh --include-symbols` if you need symbols for internal
debug builds.
