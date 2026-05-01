# Plugin Development Guide

This document explains how to build a ComCross plugin that can be discovered and loaded from the `plugins/` directory.

## 1) Plugin Structure

Each plugin lives under its own folder:

```
plugins/
  <tool-id>/
    tool.dll
    assets/
```

- `tool.dll` must embed the manifest resource.
- `assets/` is optional for icons, docs, or samples.

## 2) Manifest (Required)

The plugin manifest is an embedded resource with the name:

```
ComCross.Plugin.Manifest.json
```

Example:

```json
{
  "id": "serial.stats",
  "name": "Stats Panel",
  "version": "1.0.0",
  "targetCoreVersion": "0.3",
  "entryPoint": "ComCross.Plugins.Stats.StatsTool",
  "toolGroup": "serial",
  "permissions": ["workspace.read", "serial.read"]
}
```

### Manifest Fields

- `id`: unique plugin id (recommended prefix by domain)
- `name`: display name
- `version`: plugin version
- `targetCoreVersion`: minimum compatible core version
- `entryPoint`: fully-qualified type name for the tool
- `toolGroup`: category name
- `permissions`: declared permissions for UI display

## 3) Embedding the Manifest

In your plugin project file, embed the manifest file:

```xml
<ItemGroup>
  <EmbeddedResource Include="Resources\ComCross.Plugin.Manifest.json" />
</ItemGroup>
```

Ensure the resource name matches exactly `ComCross.Plugin.Manifest.json`.

## 4) Discovery Rules

ComCross scans the `plugins/` directory on startup. Any DLL that contains
`ComCross.Plugin.Manifest.json` will be considered a plugin.

Validation includes:
- manifest presence
- version compatibility

## 5) Host Process Isolation

Each plugin runs in its own `PluginHost` process. The main app never loads
plugin assemblies directly. This isolates crashes and allows per-plugin restarts.

Plugins should not depend on UI types. Provide data and behavior via the
notification system and any supported contracts instead.

## 6) Producer/Consumer Boundary

Bus plugins produce domain facts. The main program consumes those facts through public contracts.

For bus plugins, this means the plugin should provide:
- session display metadata, icons, and endpoint text
- reconnect policy
- parent/child session topology
- managed resource lists and action descriptors
- capability UI schema and UI state
- startup session state patches and plugin-owned storage patches

Core and Shell should not infer those facts from plugin-private parameters. If a UI or workflow needs a bus-domain fact, expose it through a PluginSdk contract instead of relying on host-side parsing.

## 7) Permissions

Permissions are declared in the manifest and are used to inform users what
the plugin is allowed to do. These do not enforce security at runtime yet.

## 8) Notifications (Optional)

Plugins can subscribe to app notifications by implementing:

```
ComCross.Shared.Services.IPluginNotificationSubscriber
```

Notifications are delivered via:

```
void OnNotification(PluginNotification notification)
```

Example: language change notification type `plugin.language.changed`, with data
key `culture`. Official plugins must handle language notifications; third-party
plugins may opt in.

Note: plugin callbacks are isolated. Exceptions are caught and the plugin will
be restarted; failures are logged.

## 9) Packaging

Place the compiled plugin DLL under its folder in `plugins/`:

```
plugins/
  serial.stats/
    tool.dll
```

## 10) Session-Owned Resources

Plugins that own transient resources under an active session can expose them through the PluginSdk resource management contract.

Typical examples:
- a TCP listener with pending clients

Use `IPluginUiStateProvider.GetUiState` with `PluginUiStateQuery.ResourceKind` and `ResourceId` to return a `PluginResourceListState`. Each `PluginManagedResourceItem` can include `PluginResourceActionDescriptor` entries that describe what the host UI can do with the resource.

Supported generic action kinds:
- `connect-scoped-resource`: promote or bind a session-owned resource through the scoped connect flow.
- `execute-action`: execute a plugin-owned action name with plugin-owned parameters.

The host UI must consume the generic descriptors and should not hardcode a plugin's private action names or payload shape.

## 11) Session Metadata

Plugins should describe the session they created through `PluginConnectResult`.

Common metadata:
- `DisplayTitle`: user-facing title for the session.
- `DisplaySubtitle`: user-facing endpoint or detail text.
- `SessionIcon`: icon resource key such as `NetworkIcon`, `ServerIcon`, `CableIcon`, or a plugin-provided icon reference.
- `CanReconnect`: whether the created session can be reconnected by the host. Omit it for the default `true`.
- `ParentSessionId`: parent session id when the new session belongs under another session.
- `ManagedResourceKinds`: resource kinds the session owns, such as `pending`.

Core stores this metadata and Shell consumes it. Core should not infer session topology from plugin id, capability id, or plugin-private parameters.
Passive child sessions created from an accepted resource should set `CanReconnect` to `false` when the host cannot actively recreate that session.

## 12) Startup Session State Initialization

Plugins can implement `IPluginSessionStateInitializer` when persisted session state needs plugin-owned validation, normalization, or migration at startup.

Core restores session descriptors first, then calls the owning plugin once during startup initialization. While this is running, the session is unavailable to Shell actions. Core applies the returned session metadata patch and session-scoped storage patch as one update, publishes `SessionUpdatedEvent`, and then marks the session ready.

The initializer receives:
- plugin id, capability id, session id, and plugin version
- the persisted session `ParametersJson`
- plugin-owned session storage as a schema version plus JSON values

The initializer returns:
- `PluginSessionMetadataPatch` for host-visible session fields such as parameters, display metadata, reconnect policy, parent session id, and managed resource kinds
- `PluginSessionStoragePatch` for plugin-owned session storage
- `Ok=false` with `Error` when the session cannot be made usable

Core owns the state transition and persistence. Plugins should not access workspace files directly for this flow. If the plugin is unavailable or the initializer fails, the session remains visible but unavailable until the user deletes it or a later startup can initialize it.

When the user deletes a session, ComCross treats that session as permanently removed. Core removes the session descriptor and deletes the plugin-owned storage file for that session. Plugins should not depend on session storage surviving session deletion.

## 13) Notes

- Keep plugins isolated from core services unless explicitly supported.
- Do not depend on internal UI types that may change between versions.

## 14) Message Frame Attributes

Bus plugins may attach small metadata attributes to received message frames through the shared-memory writer contract.

The main program validates and normalizes attributes at the contract boundary:

- schema version: `1`
- maximum attributes per frame: `8`
- key: required, UTF-8 byte length up to `32`
- key characters: lowercase ASCII letters, digits, `.`, `_`, and `-`
- value: non-null string, UTF-8 byte length up to `128`
- invalid attributes are dropped; values are never truncated
- attributes are sorted by key for storage, export, display, and extension delivery

Use attributes for domain facts that belong to one frame, such as a UDP datagram source endpoint. Do not use attributes as a general payload store or as a replacement for session metadata.

For the built-in network adapter, UDP listener sessions do not create child sessions per remote endpoint. Incoming datagrams stay on the listener session, and the sender endpoint is exposed as a message frame attribute such as `source.endpoint`. TCP listener sessions may still expose accepted connections as child sessions because TCP accepted clients are connection-oriented resources with independent lifecycles.

## 15) Transmit Targets And Send Results

Some bus sessions receive through one logical session but can send to multiple possible targets. A plugin can expose this as an optional SDK capability by implementing `IPluginTransmitTargetProvider`.

Core queries transmit targets with `PluginTransmitTargetQuery(SessionId)`. The plugin returns a `PluginTransmitTargetSnapshot`:

- `Targets`: plugin-produced target ids and labels
- `DefaultTargetId`: optional preferred target, usually the most recently active endpoint
- `RequireTargetForSend`: true when the session cannot send without an explicit target
- `UpdatedAt`: snapshot timestamp

Shell only shows a target selector when the snapshot declares targets or requires a target. Core and Shell must not infer target semantics from plugin ids, capability ids, or private connection parameters.

`PluginSendCommand` includes an optional `TransmitTargetId`. `PluginCommandResult` is the authoritative send result and can include:

- `Ok`: whether the send succeeded
- `Error` and `ErrorCode`: structured failure details for UI display
- `BytesWritten`: bytes accepted by the plugin transport
- `TargetId`: target used for the send, when applicable
- `TargetInvalidated`: true when Shell should refresh the target list

The built-in UDP listener uses this model: incoming datagrams remain on the listener session, message frames carry `source.endpoint`, and replies can be sent to a selected recent source endpoint. TCP accepted clients remain independent scoped sessions because they have connection lifecycles.
