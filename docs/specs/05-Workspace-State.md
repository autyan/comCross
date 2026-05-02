# Workspace State

## Overview

ComCross v0.4 persists workspace state as user-owned application data. The persisted model stores descriptors and UI state, not live transport connections.

Sessions are restored as disconnected descriptors at startup. Plugins may normalize plugin-owned session state during startup initialization, but ComCross does not auto-reconnect sessions.

## WorkspaceState

Current top-level fields:

- `Version`: workspace state format version. Current value: `0.4.0`.
- `WorkspaceId`: workspace identifier.
- `Workloads`: workload list.
- `ActiveWorkloadId`: workload selected on startup.
- `SessionDescriptors`: persisted session definitions.
- `UiState`: Shell UI state.
- `SendHistory`: send panel history.

v0.4 intentionally removed the legacy v0.3 `Sessions` model. Old session state is not migrated.

## Storage Locations

ComCross uses separate roots for configuration and local data.

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
```

This is a pre-stable breaking directory relocation. Old configuration,
database, log, cache, plugin session storage, and export directories are not
kept as compatibility read paths.

## Workloads

A workload groups sessions for user workflow organization. `EnsureDefaultWorkload()` creates a default workload when none exists.

Workload data is application-owned. It is not a plugin contract.

## SessionDescriptor

Session descriptors store the public information needed to show and reconnect a session manually:

- `Id`
- `Name`
- `AdapterId`
- `PluginId`
- `CapabilityId`
- `ParametersJson`
- `DisplayTitle`
- `DisplaySubtitle`
- `DisplayIcon`
- `EnableDatabaseStorage`
- `CanReconnect`
- `InitializationState`
- `InitializationError`
- `LastInitializedPluginVersion`
- `StorageSchemaVersion`
- `ParentSessionId`
- `ManagedResourceKinds`

`ParametersJson` represents the last committed connection parameters. Core/Shell should not parse it for plugin-private semantics unless a public descriptor explicitly identifies a host-consumable field, such as `PluginConnectionResourceDescriptor.ParameterKey`.

## Plugin-Owned Session Storage

Plugins can own session storage through the plugin session-state initialization contract. Core stores and deletes that data, but the plugin owns its schema and meaning.

At startup:

1. Core restores session descriptors.
2. Core asks the owning plugin to initialize session state when the plugin supports the initializer.
3. The plugin returns metadata and storage patches.
4. Core applies patches as one update and marks the session ready, or marks it unavailable on failure.

Plugins do not access workspace files directly.

## Deletion Semantics

Deleting a session is destructive.

Core removes:

- the session descriptor
- plugin-owned storage for that session
- runtime/session references managed by Core services

ComCross does not promise recovery, archival, or soft-delete behavior in v0.4.

## UI State

`UiState` stores Shell-owned presentation state:

- `ActiveSessionId`
- `AutoScroll`
- `Filters`
- `HighlightRules`

Plugin UI state is not persisted here as arbitrary Shell state. Plugins produce UI state through their contracts when Shell requests it.

## Message Storage

v0.4 message display uses the current frame store and message stream pipeline. Future v0.5 work may move display onto file-stream-backed storage to reduce memory pressure and improve durable write behavior.
