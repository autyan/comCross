# ComCross

A cross-platform embedded communication toolbox for serial, TCP, and UDP workflows.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![Platform](https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey.svg)
![Version](https://img.shields.io/badge/version-v0.4.0-blue.svg)

## Status

ComCross is preparing the v0.4.0 release. This release is the first stable, promotion-ready build focused on a reliable plugin-driven communication workspace.

The v0.4.0 workspace format is not compatible with old v0.3 session state. Existing sessions from older development builds should be recreated.

## Features

- Serial, TCP, and UDP bus adapters delivered as isolated plugins.
- Session and workload management with persisted descriptors.
- RX/TX message stream with searchable frame attributes.
- UDP listener replies through plugin-provided transmit targets.
- TCP listener accepted clients as scoped child sessions.
- Quick commands with editable user storage and localized defaults.
- Plugin settings pages, plugin-produced UI state, and plugin-owned serial scanning.
- Built-in English and Simplified Chinese localization.

## Architecture

ComCross treats the main program and bus plugins as a producer/consumer boundary:

- Bus plugins produce domain facts through `PluginSdk` contracts.
- Core coordinates plugin runtimes, session lifecycle, persistence, IPC, and message flow.
- Shell consumes Core/Shell-facing services and renders plugin-provided facts without inferring plugin-private semantics.

Project layout:

```text
ComCross/
├── src/
│   ├── Shell/          # Avalonia desktop UI using MVVM
│   ├── Core/           # Orchestration, workspace/session services, persistence, plugin runtime coordination
│   ├── Shared/         # Shared contracts, events, models, and pure helpers
│   ├── PluginSdk/      # Public plugin API and protocol-facing plugin types
│   ├── Platform/       # Platform capabilities only
│   ├── PluginHost/     # Plugin process host
│   ├── SessionHost/    # Session-scoped host process
│   ├── ExtensionHost/  # Extension host process
│   └── Plugins/        # Built-in plugins
├── docs/
│   └── specs/          # Product and system specifications
├── repo-tools/         # Repository guardrails
├── scripts/            # Packaging helpers
└── tests/              # Automated tests
```

## Quick Start

Prerequisites:

- .NET 8 SDK or newer SDK that can build `net8.0`
- Linux or Windows

Commands:

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

Repository guardrails:

```bash
bash repo-tools/check-project-boundaries.sh
bash repo-tools/check-shell-i18n.sh
bash repo-tools/check-shell-i18n-keys.sh
```

## Documentation

- [MVP / v0.4 Acceptance](docs/MVP-Acceptance.md)
- [Specification Index](docs/specs/00-Index.md)
- [MVP Scope](docs/specs/02-MVP-Scope.md)
- [System Architecture](docs/specs/03-System-Architecture.md)
- [Plugin System](docs/specs/04-Plugin-System.md)
- [Workspace State](docs/specs/05-Workspace-State.md)
- [UI/UX Specification](docs/specs/06-UI-UX-Spec.md)
- [Plugin Development Guide](docs/specs/10-Plugin-Development-Guide.md)
- [Packaging Guide](docs/specs/11-Packaging-Guide.md)

## Internationalization

ComCross uses `ILocalizationService` and `ILocalizationStrings` for Shell UI text. English strings are built into Core, and additional cultures are loaded from:

```text
src/Assets/Resources/Localization/strings.json
```

Current shipped cultures:

- `en-US`
- `zh-CN`

When adding user-visible Shell copy, use localization keys and run the i18n guardrails before committing.

## Release Notes

v0.4.0 focuses on architecture convergence and stable day-to-day use:

- Plugin IPC is consumed through Core services instead of Shell raw host calls.
- Serial port discovery belongs to the serial adapter plugin.
- Plugin settings snapshots are passed to plugin UI-state queries.
- Session persistence uses `SessionDescriptors`; old v0.3 session state is intentionally unsupported.
- Message frames support bounded attributes: max 8 entries, key <= 32 bytes, value <= 128 bytes.
- Multi-target send is plugin-declared and currently powers UDP listener replies.

## v0.5 Direction

Planned post-v0.4 work:

- File-stream-backed message storage/display for lower memory pressure.
- Complete removal of remaining controlled Shell static bridges.
- Plugin diagnostics and test tooling through explicit services.
- Protocol parsing cache and CPDL improvements.
- Release security and permission hardening.

## Runtime Baseline

ComCross targets the .NET 8 LTS runtime baseline. The baseline should remain stable for users and only change with an explicit release decision.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
