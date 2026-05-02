# ComCross Persistence Notes

This document describes ComCross v0.4 persistence. It is intentionally aligned with the current codebase and release behavior.

## Storage Overview

ComCross currently uses:

1. SQLite (`AppDatabase`) for structured application records.
2. JSON files (`ConfigService`, `WorkspaceStateStore`) for application settings and workspace state.
3. Plugin-owned session storage files managed by Core.

Default data directories:

- Windows: `%AppData%/ComCross/`
- Linux: `~/.config/ComCross/`

## SQLite Database

Database file:

```text
ComCross.db
```

Implemented by:

```text
src/Core/Services/AppDatabase.cs
```

### notifications

Stores notification-center history.

Important fields:

- `id`
- `category`
- `message_key`
- `message_args`
- `level`
- `created_at`
- `is_read`

Related service: `NotificationService`.

### log_files

Stores log file metadata and index records.

Important fields:

- `id`
- `session_id`
- `session_name`
- `file_path`
- `start_time`
- `end_time`
- `size_bytes`

Related service: `LogStorageService`.

### config_history

Stores settings snapshots whenever settings are saved.

Important fields:

- `id`
- `created_at`
- `settings_json`

Related service: `SettingsService`.

## app-settings.json

Application settings are loaded and saved through:

```text
src/Core/Services/SettingsService.cs
src/Core/Services/ConfigService.cs
src/Shared/Models/AppSettings.cs
```

Major setting groups:

- language and follow-system-language behavior
- app logs
- session logs
- notifications
- connection defaults
- display settings
- export settings
- quick command settings
- plugin settings and plugin trust settings

Settings changes are saved to JSON and also inserted into `config_history`.

## workspace-state.json

Workspace state uses the v0.4 model:

```json
{
  "Version": "0.4.0",
  "WorkspaceId": "default",
  "Workloads": [],
  "ActiveWorkloadId": null,
  "SessionDescriptors": [],
  "UiState": {
    "ActiveSessionId": null,
    "AutoScroll": true,
    "Filters": null,
    "HighlightRules": []
  },
  "SendHistory": []
}
```

Implemented by:

```text
src/Core/Models/WorkspaceState.cs
src/Core/Services/WorkspaceService.cs
src/Core/Services/WorkspaceStateStore.cs
src/Core/Services/WorkspaceMigrationService.cs
```

Important behavior:

- Sessions are persisted as `SessionDescriptors`.
- Startup restores descriptors as disconnected sessions.
- ComCross does not auto-reconnect sessions.
- v0.3 `Sessions` state is intentionally unsupported.
- `WorkspaceMigrationService` ensures required v0.4 structure such as a default workload; it does not migrate old v0.3 sessions.

## SessionDescriptor

Persisted session descriptors include:

- identity: `Id`, `Name`
- plugin ownership: `AdapterId`, `PluginId`, `CapabilityId`
- committed connection parameters: `ParametersJson`
- display metadata: `DisplayTitle`, `DisplaySubtitle`, `DisplayIcon`
- lifecycle flags: `CanReconnect`, `InitializationState`, `InitializationError`
- plugin initialization data: `LastInitializedPluginVersion`, `StorageSchemaVersion`
- topology and resources: `ParentSessionId`, `ManagedResourceKinds`
- storage preference: `EnableDatabaseStorage`

Core/Shell should not parse plugin-private connection parameters for business meaning unless the plugin exposes a public descriptor for that field.

## Plugin-Owned Session Storage

Plugins can own session storage through the startup session-state initialization contract.

Core:

- loads session descriptors
- passes plugin-owned storage to the owning plugin
- applies returned metadata/storage patches
- marks the session ready or unavailable

Plugins:

- own the schema and meaning of their storage
- do not access workspace files directly

## Deletion Semantics

Deleting a session is destructive in v0.4.

Core removes:

- the session descriptor
- plugin-owned session storage
- runtime references managed by Core services

There is no soft-delete, archive, or recovery guarantee.

## Message Data

v0.4 uses the current frame store and message-stream pipeline for message display. Message frames can contain bounded attributes:

- max 8 attributes
- key <= 32 UTF-8 bytes
- value <= 128 UTF-8 bytes

File-stream-backed message storage/display is deferred to v0.5.

## Compatibility

Current persisted format: v0.4.

Compatibility policy for this release:

- App settings use defaults for missing fields.
- SQLite tables are created with `CREATE TABLE IF NOT EXISTS`.
- v0.3 workspace session data is not migrated.
- Users upgrading from old development builds should recreate sessions.

Last updated: 2026-05-02
