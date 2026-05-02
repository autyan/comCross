# Windows And Linux Packaging Decisions

This document records the packaging decisions agreed during the
`feature/release-packaging-flow` review. It is a decision document, not a claim
that every item below is already implemented.

## 1. Packaging Toolchain

### Decision

Windows MSI packages will use WiX Toolset v7.

The repository should pin the WiX version instead of relying on the latest
global tool resolution. The preferred model is a repository-local .NET tool
manifest:

```powershell
dotnet tool restore
dotnet tool run wix -- build -acceptEula wix7 ...
```

CI may also use WiX v7, as long as the workflow explicitly accepts the WiX v7
EULA:

```powershell
wix build -acceptEula wix7 ...
```

### Rationale

WiX v4 is no longer in the current open-source consumer security servicing
window. WiX follows an annual major-version cadence:

- WiX v4: released in 2023.
- WiX v5: released in 2024.
- WiX v6: released in 2025.
- WiX v7: released in 2026.

Using WiX v7 keeps the Windows packaging toolchain on the current supported
major version. ComCross is an open-source free project, so the WiX v7 OSMF EULA
is considered acceptable for this project, but acceptance must be explicit in
local and CI builds.

### Risks

- WiX v7 requires explicit OSMF EULA acceptance. Builds must not hide this.
- WiX v7 has some breaking changes compared with older releases. Windows MSI
  packaging must be verified after the migration.
- The exact WiX version must be pinned for reproducible release builds.
- Native command failures from `dotnet`, `wix`, and signing tools must fail the
  packaging script. A packaging script must never print success after a failed
  native command.

## 2. Windows Install Scope

### Decision

Windows MSI packages will be per-user packages.

ComCross must not require administrator elevation for a normal install, update,
or uninstall.

### Rationale

ComCross is a desktop communication toolbox. It does not currently require
machine-wide services, drivers, global COM registration, or shared privileged
resources. A per-user install is better aligned with the expected user
experience and with future plugin update behavior.

Per-machine MSI installation was validated during the Windows smoke test and
failed without elevation. That is not the desired default user experience.

## 3. Windows Directory Layout

### Decision

The main program, user data, and plugin packages are separate directory domains.

Recommended Windows layout:

```text
Main program:
%LocalAppData%\Programs\ComCross\

User configuration:
%AppData%\ComCross\

User-local data:
%LocalAppData%\ComCross\

Runtime plugin packages:
%LocalAppData%\ComCross\plugins\
```

The main program directory contains the Shell, Core-facing host executables,
runtime dependencies, and bundled plugin seed packages. It is owned by the
installer.

The plugin directory contains plugin packages that the application runtime
loads. Official plugins are not special at runtime. They are plugin packages in
the same plugin directory as any future third-party plugins.

The user configuration directory contains roaming or user-profile configuration
such as:

```text
%AppData%\ComCross\app-settings.json
%AppData%\ComCross\workspace-state.json
%AppData%\ComCross\toolset.json
```

The user-local data directory contains machine-local state such as plugin
packages, logs, caches, and other non-roaming data:

```text
%LocalAppData%\ComCross\plugins\
%LocalAppData%\ComCross\logs\
%LocalAppData%\ComCross\cache\
%LocalAppData%\ComCross\data\
```

### Rationale

`%LocalAppData%\Programs\ComCross` is the per-user equivalent of an application
install root. It should be treated as installer-owned program content.

Plugin packages are executable application extensions and should not be roaming
configuration. They are machine-local, architecture-sensitive, and may contain
native assets. Therefore they belong under `%LocalAppData%\ComCross\plugins`,
not `%AppData%\ComCross\plugins`.

Separating plugin packages from the main program from the beginning avoids a
future migration when plugin updates, plugin rollback, plugin signing, or a
plugin marketplace are added.

## 4. Linux Directory Layout

### Decision

Linux packages will use system package locations for the main program and XDG
locations for user configuration, data, cache, logs, and runtime plugins.

Recommended Linux layout:

```text
Main program:
/opt/comcross/

Launcher:
/usr/bin/comcross

Desktop entry:
/usr/share/applications/comcross.desktop

Icon:
/usr/share/icons/hicolor/256x256/apps/comcross.png

User configuration:
${XDG_CONFIG_HOME:-$HOME/.config}/ComCross/

User-local data:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/

User cache/logs:
${XDG_CACHE_HOME:-$HOME/.cache}/ComCross/

Runtime plugin packages:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

### Rationale

DEB and RPM packages install machine-owned program files. They should not write
directly into arbitrary users' home directories at package install time.

AppImage packages are also unsuitable as a writable plugin location because the
AppImage itself is not the long-term mutable plugin store.

The XDG data directory is the right cross-distribution location for user-local
plugin packages. The XDG config directory is the right location for user
configuration. Cache and logs should be kept separate so users and tools can
clean them without deleting configuration or plugin packages.

## 5. Official Plugin Distribution Model

### Decision

Official plugins are normal plugin packages.

They have no special runtime treatment. They are published and updated together
with the main program for now, but they still live in the runtime plugin
directory:

```text
Windows:
%LocalAppData%\ComCross\plugins\<plugin-id>-<stable-hash>\

Linux:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/<plugin-id>-<stable-hash>/
```

### Bundled Seed Model

Install packages should carry official plugin packages as bundled seed content
under the main program directory:

```text
Windows:
%LocalAppData%\Programs\ComCross\bundled-plugins\

Linux:
/opt/comcross/bundled-plugins/
```

On startup, Core synchronizes bundled official plugin packages into the runtime
plugin directory. The runtime loads plugins only from the runtime plugin
directory.

### Rationale

This keeps a single plugin model from day one:

- Official plugins are plugins.
- Future third-party plugins are plugins.
- The runtime loads one plugin directory.
- Plugin update, rollback, disablement, and signature verification can evolve
  without changing the directory contract.

There is no plugin marketplace or independent automatic plugin update mechanism
in the current scope. Official plugins are still synchronized from the main
application release package. The design simply avoids coupling runtime plugin
loading to the main program install directory.

## 6. Required Code Changes

The directory model above requires code changes.

The current implementation has several install-layout assumptions:

- `ConfigService` defaults to `Environment.SpecialFolder.ApplicationData` plus
  `ComCross`.
- `AppDatabase` currently follows the config directory.
- `PluginManagerService` scans `Path.Combine(AppContext.BaseDirectory,
  "plugins")`.
- `PluginManagerViewModel` displays `Path.Combine(AppContext.BaseDirectory,
  "plugins")`.
- App and plugin-host log services default to `ApplicationData` based paths.

Required changes:

1. Add a single Core-owned path service, for example `ComCrossPathService`.
2. Expose separate paths for:
   - install directory
   - bundled plugin seed directory
   - runtime plugin directory
   - config directory
   - local data directory
   - cache directory
   - log directory
3. Update `ConfigService`, `AppDatabase`, log services, plugin discovery, and
   Shell plugin management UI to use the path service.
4. Add a bundled-plugin synchronization service.
5. Change the packaging scripts so official plugins are packaged as bundled
   seed content, then synchronized into the runtime plugin directory.
6. Update the public documentation to describe the new directory contract.

## 7. Migration Strategy

ComCross is pre-1.0, so breaking changes are acceptable when they close
architecture gaps, but they must not be silent.

Recommended migration approach:

- For configuration files currently under `%AppData%\ComCross`, keep compatible
  reads where possible.
- For plugin packages currently under `AppContext.BaseDirectory\plugins`, treat
  them as legacy development or previous package output. On startup, synchronize
  bundled official plugins into the new runtime plugin directory.
- For databases and logs, choose one of two strategies before implementation:
  - keep existing locations for v0.5 and only move plugin packages now; or
  - perform a one-time migration into the new config/data/cache split.

The recommended first implementation scope is to move plugin runtime loading
and packaging first, while keeping existing config/database behavior compatible.
The config/data/cache split can be implemented in a second scope if needed.

## 8. Verification Requirements

Windows verification must cover more than MSI generation.

Minimum Windows checks:

1. Build `win-x64` MSI.
2. Build `win-arm64` MSI.
3. Install `win-x64` per-user without administrator elevation.
4. Verify main program files under `%LocalAppData%\Programs\ComCross`.
5. Verify official plugin packages under `%LocalAppData%\ComCross\plugins`.
6. Launch from the Start Menu shortcut.
7. Verify the Shell window opens.
8. Verify plugin discovery reports the official plugins as loaded or disabled
   according to settings.
9. Verify host processes start and shut down cleanly.
10. Close the application and verify there are no orphaned `ComCross.*`
    processes.
11. Uninstall and verify installer-owned files and shortcuts are removed.
12. Verify user configuration and plugin data retention follows the documented
    uninstall policy.

Linux verification must cover:

1. Build DEB/RPM packages.
2. Build AppImage packages.
3. Install DEB/RPM on a Linux test machine or container with a desktop-capable
   smoke path.
4. Verify `/opt/comcross` contains the main program and bundled plugin seed.
5. Verify runtime plugin packages are synchronized into the XDG data directory.
6. Launch through `/usr/bin/comcross`.
7. Verify plugin discovery and host startup.
8. Uninstall the package and verify system-owned files are removed without
   deleting user-owned configuration and plugin data unless explicitly requested.

## 9. Open Implementation Questions

- Should uninstall remove runtime official plugins, or retain all user-local
  plugin packages by default?
- Should the bundled-plugin synchronization overwrite same-version official
  plugin packages, or only replace when the bundled version/hash is newer?
- Should user-installed plugins be allowed to override official plugin ids in
  the first implementation, or should official plugin ids be protected until
  plugin signing is fully enforced?
- Should `ComCross.db` stay in the config directory for now, or move to the
  local data directory with a one-time migration?
