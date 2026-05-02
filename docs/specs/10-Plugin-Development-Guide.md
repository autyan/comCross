# Plugin Development Guide

This document explains how to build a ComCross plugin for the v0.4 plugin architecture.

## 1) Plugin Structure

During development and release packaging, each plugin is placed under an isolated plugin folder:

```text
plugins/
  <plugin-id>-<stable-hash>/
    <plugin assembly and dependencies>
```

The exact release folder name is produced by packaging scripts from the plugin id and a stable hash.

## 2) Manifest

Each plugin assembly must embed:

```text
ComCross.Plugin.Manifest.json
```

Example:

```json
{
  "id": "network.adapter",
  "name": "Network Adapter",
  "version": "0.4.0",
  "targetCoreVersion": "0.4",
  "entryPoint": "ComCross.Plugins.Network.NetworkBusAdapterPlugin",
  "pluginType": "BusAdapter",
  "toolGroup": "network",
  "permissions": ["network.connect", "network.send", "workspace.read"]
}
```

Manifest fields:

- `id`: stable plugin id.
- `name`: fallback display name.
- `version`: plugin version.
- `targetCoreVersion`: minimum compatible core version.
- `entryPoint`: fully qualified plugin type name.
- `pluginType`: plugin role, such as `BusAdapter` or `Extension`.
- `toolGroup`: category for grouping.
- `permissions`: user-facing permission declarations.
- `settingsPages`: optional plugin settings pages rendered by Shell.
- `i18n`: optional plugin-provided localized strings.

## 3) Embedding The Manifest

In the plugin project file:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\ComCross.Plugin.Manifest.json" />
</ItemGroup>
```

The embedded resource must end with `ComCross.Plugin.Manifest.json`.

## 4) Discovery And Isolation

ComCross scans the `plugins/` directory on startup. A DLL with an embedded manifest is considered a plugin candidate.

Validation includes:

- manifest presence
- entry point type
- version compatibility
- capability and schema shape

Plugins run out of process through host executables. The main app does not load plugin assemblies directly.

## 5) Producer/Consumer Boundary

Bus plugins produce domain facts. Core and Shell consume those facts through public contracts.

For bus plugins, plugin-produced facts include:

- capability schemas and UI schemas
- plugin UI state
- session display metadata
- reconnect policy
- parent/child session topology
- managed resource descriptors and actions
- startup session-state patches
- transmit targets
- message frame attributes

Core and Shell must not infer bus-domain facts from plugin-private parameters. If a workflow needs a fact, add or use an SDK contract.

## 6) Capabilities

Bus plugins expose capabilities through `IPluginCapabilityProvider`.

Capabilities can declare:

- id, name, description, and icon
- JSON schema and UI schema
- default parameters
- shared-memory request
- session host model
- exclusive connection resource descriptor

`PluginConnectionResourceDescriptor` is a generic host hint. It lets Shell/Core detect conflicts for declared resources, such as the serial `port` parameter. The plugin must still validate the final connection request.

## 7) UI State And Settings

Plugins can implement `IPluginUiStateProvider` to produce UI state for Shell.

`PluginUiStateQuery` can include:

- `CapabilityId`
- `SessionId`
- `ViewKind`
- `ViewInstanceId`
- `ResourceKind`
- `ResourceId`
- `Settings`

Core passes a read-only settings snapshot for plugin settings pages. Snapshot keys use:

```text
{settingsPageId}.{fieldKey}
```

Example: the serial plugin owns serial port scanning. It reads `serial-scan.scanPatterns` from the settings snapshot, produces a `ports` UI state, and handles the refresh action. Shell only renders that state and dispatches the action.

## 8) Session Metadata

Plugins should describe created sessions through `PluginConnectResult`.

Common metadata:

- `DisplayTitle`
- `DisplaySubtitle`
- `SessionIcon`
- `CanReconnect`
- `ParentSessionId`
- `ManagedResourceKinds`

Core persists these fields in `SessionDescriptor`; Shell renders them directly.

Passive TCP accepted clients should set `CanReconnect` to `false` when the host cannot actively recreate them.

## 9) Session-Owned Resources

Plugins that own transient resources under an active session can expose them through the resource management contract.

Use `IPluginUiStateProvider.GetUiState` with `ResourceKind` and `ResourceId` to return resource state. Resource items can include action descriptors.

Supported generic action kinds:

- `connect-scoped-resource`: promote or bind a session-owned resource through the scoped connect flow.
- `execute-action`: execute a plugin-owned action name with plugin-owned parameters.

The host UI consumes descriptors generically and must not hardcode plugin-private action names or payloads.

## 10) Startup Session State Initialization

Plugins can implement `IPluginSessionStateInitializer` when persisted session state needs plugin-owned validation, normalization, or migration at startup.

Core restores descriptors first, then calls the owning plugin once during startup initialization. While initialization is running, Shell treats the session as unavailable. Core applies returned metadata and storage patches as one update, then marks the session ready or unavailable.

The initializer receives:

- plugin id, capability id, session id, and plugin version
- persisted `ParametersJson`
- plugin-owned session storage schema version and JSON values

It can return:

- `PluginSessionMetadataPatch`
- `PluginSessionStoragePatch`
- `Ok=false` with `Error`

Plugins do not access workspace files directly. Core owns persistence and state transitions.

Deleting a session is destructive. Core removes the descriptor and plugin-owned session storage for that session.

## 11) Message Frame Attributes

Bus plugins may attach small metadata attributes to message frames.

Limits:

- schema version: `1`
- maximum attributes per frame: `8`
- key: required, lowercase ASCII letters, digits, `.`, `_`, and `-`; UTF-8 byte length <= `32`
- value: non-null string; UTF-8 byte length <= `128`

Invalid attributes are dropped. Values are never truncated. Attributes are sorted by key for stable storage, display, search, export, and extension delivery.

Use attributes for frame-level facts, such as a UDP datagram source endpoint. Do not use attributes as payload storage or as a replacement for session metadata.

The built-in UDP listener keeps incoming datagrams on the listener session and exposes sender endpoint as `source.endpoint`. UDP client frames do not add `source.endpoint` because the connected endpoint is already session-level metadata.

Frame direction is not an attribute. It is represented by the frame/message direction field.

## 12) Transmit Targets And Send Results

Plugins can implement `IPluginTransmitTargetProvider` when a session can send to multiple targets.

Core queries `PluginTransmitTargetSnapshot`, which includes:

- `Targets`
- `DefaultTargetId`
- `RequireTargetForSend`
- `UpdatedAt`

Shell shows a target selector only when the plugin declares targets or requires one.

`PluginSendCommand` includes optional `TransmitTargetId`.

`PluginCommandResult` is the authoritative send result and can include:

- `Ok`
- `Error`
- `ErrorCode`
- `BytesWritten`
- `TargetId`
- `TargetInvalidated`

For multi-target sending, `PluginTransmitTarget.Attributes` carries attributes that Core copies onto the mirrored TX frame after a successful send.

## 13) Notifications

Plugins can subscribe to app notifications by implementing:

```text
ComCross.Shared.Services.IPluginNotificationSubscriber
```

Language change notifications use type `plugin.language.changed` and data key `culture`.

Plugin callback exceptions are isolated and logged.

## 14) Permissions

Permissions are declared in the manifest and displayed to users. v0.4 does not yet enforce runtime permission isolation. Release security hardening is planned after v0.4.

## 15) Notes

- Keep plugins isolated from Core and Shell internals.
- Do not reference Shell UI types from plugins.
- Prefer SDK contracts over host-side special cases.
