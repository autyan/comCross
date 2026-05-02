# System Architecture

## Core Principle

ComCross has a clear producer/consumer boundary between the main program and bus plugins.

- Bus plugins produce domain facts through `PluginSdk` contracts.
- Core consumes those facts, coordinates lifecycle and persistence, and exposes application services.
- Shell consumes Core/Shell-facing services and renders facts without inferring plugin-private semantics.

If Core or Shell needs a bus-domain fact, the fact must be exposed through a contract instead of being inferred from plugin ids, capability ids, parameter names, or transport-specific heuristics.

## Modules

- `Shell`: Avalonia desktop UI using MVVM. It owns visual composition, user interaction, dialogs, and Shell-specific view models.
- `Core`: business orchestration, workspace/session services, persistence, plugin runtime coordination, localization, and message stream services.
- `Shared`: shared contracts, events, models, and small pure helpers used across process boundaries.
- `PluginSdk`: public plugin-facing API. Built-in and external plugins use the same SDK-facing contracts.
- `Platform`: platform capabilities only.
- `PluginHost`: process host for plugin instances and plugin IPC handlers.
- `SessionHost`: session-scoped host process.
- `ExtensionHost`: extension host process.
- `Plugins`: built-in plugins packaged through the same plugin model expected of external plugins.

## Runtime Flow

1. Shell starts `AppHost`.
2. Core loads settings, localization, workspace state, plugin runtimes, and built-in plugin packages.
3. Core discovers plugin capabilities and exposes Shell-facing adapter/session services.
4. Shell renders plugin-produced schemas, UI state, session metadata, resource descriptors, and transmit targets.
5. User actions enter Core services.
6. Core sends typed plugin commands/queries through `PluginHostProtocolService`.
7. Plugins return command results, state snapshots, session metadata patches, frame data, and lifecycle events.
8. Core validates contract shape, updates workspace/session state, mirrors messages, and publishes Shell-facing state.

## Shell Boundaries

Shell should not call raw plugin-host IPC. The normal path is:

```text
Shell View/ViewModel -> Shell/Core service -> Core plugin protocol service -> PluginHost -> Plugin
```

The remaining Shell static bridges are controlled exceptions for v0.4:

- `MessageBoxService`
- `ShellUiServices`
- `App.ServiceProvider` access through Shell base view/window types

These exceptions exist to manage current Avalonia view lifecycle constraints. Full removal is a v0.5 architecture goal.

## Message Flow

Plugins write received frames through the shared-memory/frame contracts. Core normalizes frame data and attributes, stores in the in-memory frame store, and pumps to Shell message streams.

For outgoing data:

1. Shell submits a send request through the active session service.
2. Core sends `PluginSendCommand` to the owning plugin.
3. The plugin returns `PluginCommandResult`.
4. Core mirrors a TX frame only after successful send.
5. If a transmit target was selected, Core copies the target attributes onto the mirrored TX frame.

Frame direction is represented by the frame/message direction field, not by a message attribute.

## Persistence Flow

Workspace state stores session descriptors, workloads, UI state, and send history. Sessions are restored as disconnected descriptors at startup; no automatic reconnect is assumed.

Plugins may implement startup session-state initialization to normalize plugin-owned persisted session state. Core applies returned metadata/storage patches and marks the session ready or unavailable.

Session deletion is destructive: Core removes the session descriptor and deletes the plugin-owned storage file for that session.
