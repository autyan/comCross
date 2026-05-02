# Startup, Instance Identity, And Signing Design

> Documentation generation notice: this document was drafted with AI
> assistance. Treat it as a design record to review, test, and revise before
> implementation. Security-sensitive details require human review.

This document records the planned product-entry, multi-instance, directory,
host, plugin signing, and Windows signing strategy for ComCross.

## 1. Goals

- Keep OS-specific user-directory resolution outside Core.
- Support side-by-side Stable, Dev, and EAP installations.
- Make `ComCross.Startup` the user-facing entry point.
- Allow `ComCross.Shell` to launch directly while still knowing its instance.
- Enforce one running process per application instance while allowing different
  instances to run side by side.
- Make host processes explicitly instance-aware.
- Require official plugin package signatures outside Dev.
- Keep Windows self-signed package signing honest and verifiable during the
  current pre-public stage.
- Keep future migration paths open without overbuilding the first
  implementation.

## 2. Non-Goals

- No full version manager UI in the first Startup implementation.
- No third-party plugin publishing model yet.
- No automatic cleanup of user data on uninstall.
- No compatibility migration from pre-stable directory layouts.
- No public-trust Windows code-signing certificate in the current scope.
- No promise that self-signed Windows packages bypass SmartScreen or Smart App
  Control.

## 3. Layer Responsibilities

### Platform

`src/Platform` owns OS-specific user-directory resolution.

It should expose a small provider such as:

```csharp
public interface IPlatformUserDirectoryProvider
{
    string ConfigHome { get; }
    string LocalDataHome { get; }
    string CacheHome { get; }
}
```

Windows mapping:

```text
ConfigHome    -> %AppData%
LocalDataHome -> %LocalAppData%
CacheHome     -> %LocalAppData%
```

Linux mapping:

```text
ConfigHome    -> ${XDG_CONFIG_HOME:-$HOME/.config}
LocalDataHome -> ${XDG_DATA_HOME:-$HOME/.local/share}
CacheHome     -> ${XDG_CACHE_HOME:-$HOME/.cache}
```

Platform must not know ComCross product names, channels, plugin directories, or
database directories.

### Core

Core owns the ComCross directory contract and composes product paths from:

- platform user directories;
- application instance identity.

`ComCrossPathService` remains the Core-owned directory contract service, but it
must not contain OS checks.

### Startup

`ComCross.Startup` is the user-facing entry point.

The first implementation is a minimal GUI launcher:

- show a small splash window;
- read the instance manifest;
- enforce single-instance behavior for that instance;
- locate and start Shell from the same install directory by default;
- show an error dialog when Shell cannot be started;
- write startup logs;
- exit after handing off to Shell, unless later product requirements require a
  resident launcher.

Startup should reference as little as possible. The first implementation should
prefer no Core reference. It may carry a minimal private fallback path resolver
until a dedicated lightweight bootstrap/shared package exists.

### Shell

Shell remains the UI application.

Shell must be able to launch directly. Direct launch reads the same instance
manifest from its install directory, applies the same instance identity, and
uses the same directory contract as Startup-launched Shell.

Shell is not responsible for version-selection UI.

### Host Processes

`PluginHost`, `SessionHost`, and `ExtensionHost` must be explicitly
instance-aware. They should receive instance and path arguments from Core rather
than independently deriving normal runtime directories.

## 4. Application Instance Manifest

Each install directory carries:

```text
ComCross.Instance.json
```

Example Stable manifest:

```json
{
  "schemaVersion": 1,
  "product": "ComCross",
  "channel": "Stable",
  "directoryName": "ComCross",
  "instanceId": "comcross-stable",
  "schemaLine": "v0"
}
```

Example Dev manifest:

```json
{
  "schemaVersion": 1,
  "product": "ComCross",
  "channel": "Dev",
  "directoryName": "ComCrossDev",
  "instanceId": "comcross-dev",
  "schemaLine": "v0"
}
```

Accepted initial channels:

```text
Stable -> ComCross
Dev    -> ComCrossDev
EAP    -> ComCrossEAP
```

The manifest is not a security boundary. In a per-user install, the install
directory may be user-writable. Security decisions such as plugin trust must not
depend only on this file.

## 5. Instance Resolution

Formal launch path:

1. `ComCross.Startup` starts.
2. Startup reads `ComCross.Instance.json` from `AppContext.BaseDirectory`.
3. Startup passes the manifest path to Shell:

   ```text
   --instance-manifest <install-dir>/ComCross.Instance.json
   ```

4. Shell reads the manifest and initializes Core with that identity.

Direct Shell launch:

1. Shell reads `--instance-manifest` when present.
2. Otherwise Shell reads `ComCross.Instance.json` from its own
   `AppContext.BaseDirectory`.
3. If the manifest is missing, Shell may use a build-default fallback:
   - Debug fallback: Dev.
   - Release fallback: Stable.

Environment variables are development hooks only. They are not the formal
identity mechanism.

Allowed development hooks:

```text
COMCROSS_INSTANCE_MANIFEST
COMCROSS_CHANNEL
COMCROSS_SHELL_PATH
```

Priority:

```text
explicit command-line argument
> colocated ComCross.Instance.json
> development environment hook
> build fallback
```

## 6. Directory Contract

Core composes paths as:

```text
ConfigDirectory      = <ConfigHome>/<DirectoryName>
LocalDataDirectory   = <LocalDataHome>/<DirectoryName>
CacheDirectory       = <CacheHome>/<DirectoryName>
DatabaseDirectory    = <LocalDataDirectory>/data
LogDirectory         = <LocalDataDirectory>/logs
StartupLogDirectory  = <LocalDataDirectory>/logs/startup
AppLogDirectory      = <LocalDataDirectory>/logs/app
PluginHostLogDir     = <LocalDataDirectory>/logs/plugin-host
ExportDirectory      = <LocalDataDirectory>/exports
RuntimePluginsDir    = <LocalDataDirectory>/plugins
PluginSessionStorage = <LocalDataDirectory>/plugin-session-storage
BundledPluginsDir    = <InstallDirectory>/bundled-plugins
```

Windows examples:

```text
Stable:
%AppData%\ComCross\
%LocalAppData%\ComCross\

Dev:
%AppData%\ComCrossDev\
%LocalAppData%\ComCrossDev\
```

Linux examples:

```text
Stable:
${XDG_CONFIG_HOME:-$HOME/.config}/ComCross/
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/
${XDG_CACHE_HOME:-$HOME/.cache}/ComCross/

Dev:
${XDG_CONFIG_HOME:-$HOME/.config}/ComCrossDev/
${XDG_DATA_HOME:-$HOME/.local/share}/ComCrossDev/
${XDG_CACHE_HOME:-$HOME/.cache}/ComCrossDev/
```

This remains a pre-stable breaking directory contract. Old paths are not
compatibility read paths.

## 7. Single-Instance Contract

Single-instance behavior is scoped to an application instance.

Required behavior:

```text
ComCross Stable: one running instance.
ComCross Dev: one running instance.
ComCross EAP: one running instance.

Stable and Dev may run at the same time.
Stable and EAP may run at the same time.
Dev and EAP may run at the same time.
```

Lock key:

```text
<instanceId>
```

Examples:

```text
comcross-stable
comcross-dev
comcross-eap
```

Recommended implementation:

- Windows: named mutex scoped by `instanceId`.
- Linux: lock file under the instance local-data directory.

Startup must enforce this contract. Shell must also enforce it so users cannot
bypass the contract by launching Shell directly.

The first implementation may show a simple "ComCross is already running" error.
Later implementations may focus an existing window or forward activation.

## 8. Startup Shell Location

Default Shell location is same-directory:

```text
<startup-base-dir>/ComCross.Shell
<startup-base-dir>/ComCross.Shell.exe
```

Startup should keep small hook points for future evolution:

```text
--shell-path <path>
COMCROSS_SHELL_PATH=<path>
```

The hook is for development, diagnostics, and future launcher/toolbox
integration. Normal installed shortcuts must not depend on it.

## 9. Startup UI And Logs

Startup first version is a minimal GUI.

Required UI:

- splash or small launch window;
- launch status text;
- error dialog if Shell cannot be started.

Required log:

```text
<LocalDataDirectory>/logs/startup/startup.log
```

Startup logs should include:

- resolved install directory;
- manifest path;
- resolved channel and instance id;
- resolved Shell path;
- Shell start success or failure;
- lock acquisition failure;
- exception details.

If Startup cannot resolve the instance identity, it should fall back to Stable
for first-version user-facing behavior and log the fallback.

## 10. Host Instance Awareness

Host processes must not infer normal user directories independently.

Core should pass at least:

```text
--instance-id <instanceId>
--log-dir <path>
```

Candidate future arguments:

```text
--cache-dir <path>
--runtime-plugin-dir <path>
```

Host use cases for instance identity:

- log attribution;
- crash dump or diagnostic naming;
- future IPC name scoping;
- future shared-memory name scoping;
- security and audit diagnostics.

IPC and shared-memory names should be reviewed and, where needed, include
`instanceId` so Stable and Dev can run concurrently without name collisions.

Host fallback path derivation is allowed only for direct/manual host execution
diagnostics. It must not be the normal path when started by Core.

## 11. Plugin Runtime Trust Model

The runtime plugin root is user-writable executable content:

```text
<LocalDataDirectory>/plugins
```

This is intentional for future plugin extensibility, but it is a security risk
if arbitrary DLLs are loaded. First-version policy:

```text
Stable: require official plugin package signatures.
EAP: require official plugin package signatures.
Dev: allow unsigned plugin packages, but log a warning and show diagnostic state.
```

Official plugins are the only supported plugin distribution model until a
future third-party plugin policy is designed.

## 12. Plugin Package Signing

Plugin package signing is separate from release artifact signing and Windows
code signing.

Separate keys are required:

```text
release artifact signing key
Windows code signing certificate or test certificate
official plugin package signing key
```

The plugin signing private key must never be committed. The plugin signing
public key may be committed.

Recommended first implementation:

- algorithm: RSA-PSS with SHA-256, unless a later implementation chooses
  Ed25519 and accepts the dependency/tooling cost;
- public keys stored in a trusted plugin key registry in the repository;
- signatures stored per plugin package.
- runtime enforcement is controlled by plugin trust policy. Development may
  leave enforcement disabled or explicitly allow unsigned plugin ids, but
  Stable/EAP enforcement must not rely on an unsigned allow-list.

Signature file:

```text
ComCross.Plugin.Signature.json
```

Example:

```json
{
  "schemaVersion": 1,
  "keyId": "comcross-plugin-official-2026-01",
  "pluginId": "serial.adapter",
  "version": "0.5.0",
  "algorithm": "RSA-PSS-SHA256",
  "signedAt": "2026-05-02T00:00:00Z",
  "files": [
    {
      "path": "ComCross.Plugins.Serial.dll",
      "sha256": "..."
    }
  ],
  "signature": "..."
}
```

Signature payload should be deterministic canonical JSON without the
`signature` field.

The first verifier canonicalizes the payload by writing the fields in this
order:

```text
schemaVersion, keyId, pluginId, version, algorithm, signedAt, files
```

File entries are normalized to slash-separated relative paths and sorted by
path before verification.

Verification must check:

- signature schema version;
- key id is trusted for official plugins;
- algorithm is supported;
- plugin id matches the embedded plugin manifest;
- package file hashes match the signature file;
- every package file except `ComCross.Plugin.Signature.json` is listed in the
  signature payload;
- signature validates with the trusted official plugin public key.

Stable/EAP failure behavior:

- unsigned or invalid-signature plugin packages are not loaded;
- Shell still starts;
- plugin manager reports the plugin as failed or blocked with a clear
  diagnostic.

Dev behavior:

- unsigned packages may load;
- warnings are logged;
- diagnostics should make unsigned loading visible.

## 13. Plugin Signing Keys

Public key material can be committed. Private key material must be generated and
stored outside the repository.

Recommended public key directory:

```text
security/keys/
```

Candidate files:

```text
security/keys/comcross-release-2026.pub.asc
security/keys/comcross-plugin-official-2026.pub.pem
security/keys/comcross-windows-test-codesign-2026.cer
```

The initial public key record is documented in:

```text
docs/security/signing-keys.md
```

Private key material should live in an encrypted operator-controlled location,
for example:

```text
<encrypted-key-store>/private/
```

The repository should provide scripts that generate keys directly into an
operator-provided output directory. Scripts must not default to writing private
keys into the repository.

## 14. Windows Signing Strategy

Current stage:

- Windows packages may use a self-signed test certificate.
- The certificate is not trusted by Windows by default.
- SmartScreen, Smart App Control, Defender, or enterprise policy may warn or
  block the installer or application.
- Users must not be told that self-signing is equivalent to public-trust code
  signing.

Required user-facing notice:

```text
ComCross currently uses a self-signed Windows test certificate. This
certificate is not trusted by Windows by default. SmartScreen, Smart App
Control, or antivirus software may warn or block the installer.

Do not bypass these warnings unless you understand the risk and have verified
the release through the checksums and release signature published in this
repository.

The self-signed certificate only helps identify packages produced by the current
ComCross release process after you explicitly trust the project key. It does
not provide Microsoft or CA-backed publisher reputation.
```

Chinese notice:

```text
ComCross 当前使用自签名 Windows 测试证书。该证书不会被 Windows 默认信任，
因此 SmartScreen、Smart App Control 或杀毒软件可能提示风险或阻止安装。

请不要在未验证来源的情况下直接绕过这些警告。安装前应使用本仓库发布的
校验和与发布签名验证安装包。

自签名证书只能在你信任 ComCross 项目公钥和发布流程后，辅助确认安装包
来自当前项目发布流程；它不提供 Microsoft 或 CA 背书的发布者信誉。
```

Future public preview:

- evaluate Azure Artifact Signing if individual/open-source eligibility and
  pricing are acceptable;
- otherwise evaluate traditional OV/EV code-signing certificates;
- keep plugin signing separate from Windows code signing.

## 15. Release Verification Path

Every public release should publish:

```text
ComCross-<version>-win-x64.msi
ComCross-<version>-win-arm64.msi
SHA256SUMS
SHA256SUMS.asc
```

Users with GPG:

```bash
gpg --import security/keys/comcross-release-2026.pub.asc
gpg --verify SHA256SUMS.asc SHA256SUMS
sha256sum -c SHA256SUMS
```

Windows users without GPG can at minimum compare SHA-256:

```powershell
Get-FileHash .\ComCross-<version>-win-x64.msi -Algorithm SHA256
```

The documentation must clearly state that checksum-only verification detects
download corruption or mismatch but does not by itself prove release identity
unless the checksum file is verified through a trusted signature path.

## 16. Atomic Bundled Plugin Synchronization

Current desired sync model:

```text
<InstallDirectory>/bundled-plugins
  -> <LocalDataDirectory>/plugins
```

Delete-then-copy is unsafe. If the process is interrupted, runtime plugins can
be missing or half-copied.

Required staged replace model:

1. Copy bundled plugin package to a staging directory:

   ```text
   <RuntimePluginsDir>/.staging/<plugin-folder>.<operation-id>
   ```

2. Verify the staged package:
   - manifest can be read;
   - required entry assembly exists;
   - signature is valid when required by the instance channel.

3. Move existing runtime package to trash:

   ```text
   <RuntimePluginsDir>/.trash/<plugin-folder>.<operation-id>
   ```

4. Move staged package into final runtime package path.

5. Clean old trash best-effort.

If any step fails before final replacement, the previous runtime package should
remain loadable.

Windows directory replacement cannot be treated as a simple atomic overwrite.
The implementation must handle existing target directories through staged moves.

## 17. Uninstall Policy

First-version uninstall policy:

- uninstall removes installer-owned files and shortcuts;
- uninstall does not remove user configuration, databases, logs, cache,
  runtime plugins, or plugin session storage;
- all installed versions may be uninstalled together by the user through the
  platform package manager or uninstall UI;
- user-data cleanup can be added later as an explicit option.

This policy must be visible in Windows and Linux packaging documentation before
public release.

## 18. Implementation Feature Plan

Recommended order from `develop`:

1. `feature/platform-user-directories`
   - Move OS directory rules into Platform.
   - Make Core consume the Platform provider.
   - Keep Platform unaware of ComCross product names.

2. `feature/application-instance-identity`
   - Add `ComCross.Instance.json` generation and parsing.
   - Add Stable/Dev/EAP identity mapping.
   - Add per-instance single-instance contract.
   - Make Shell direct launch resolve its identity.

3. `feature/startup-launcher-entrypoint`
   - Add `ComCross.Startup`.
   - Implement minimal GUI splash and error dialog.
   - Implement startup logging.
   - Launch same-directory Shell by default.
   - Keep `--shell-path` and `COMCROSS_SHELL_PATH` hooks.

4. `feature/host-instance-awareness`
   - Pass `--instance-id` and `--log-dir` to host processes.
   - Review IPC/shared-memory names for instance scoping.

5. `feature/plugin-package-signing`
   - Add official plugin signing public key registry.
   - Generate/verify `ComCross.Plugin.Signature.json`.
   - Fail closed in Stable/EAP.
   - Allow unsigned in Dev with diagnostics.

6. `feature/atomic-plugin-sync`
   - Implement staged plugin package replacement.
   - Keep old runtime package when staged copy or verification fails.

7. Release packaging follow-up:
   - Package Startup.
   - Point shortcuts/desktop entries/MSI launch target to Startup.
   - Generate instance manifests for each channel.
   - Publish verification artifacts and signing-key documentation.

## 19. Acceptance Criteria

- Core no longer contains OS-specific user-directory selection.
- Stable, Dev, and EAP use separate config/local data/cache roots.
- Stable and Dev can run at the same time.
- Two Stable processes cannot run at the same time.
- Startup is the documented user entry point.
- Shell can launch directly and resolve its instance identity.
- Host logs and future IPC diagnostics include instance identity.
- Stable/EAP do not load unsigned plugins.
- Dev can load unsigned plugins with warnings.
- Plugin package replacement does not delete the previous package before the new
  package is staged and verified.
- Windows self-signed signing is documented as a risk, not a trust guarantee.
- Release verification documents include a signature-backed validation path.
