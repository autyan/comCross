# Plugin System

## Goals

- Keep built-in and external plugins behind the same public SDK contracts.
- Isolate plugin failures from the main UI process.
- Let plugins produce bus-domain facts while Core/Shell consume those facts generically.
- Avoid Shell/Core hardcoding plugin-private semantics.

## Plugin Layout

Runtime plugin packages live under the user-local plugin root:

```text
<runtime-plugin-root>/
  <plugin-id>-<stable-hash>/
    <plugin assembly and dependencies>
```

Runtime plugin roots:

```text
Windows:
%LocalAppData%\ComCross\plugins\

Linux:
${XDG_DATA_HOME:-$HOME/.local/share}/ComCross/plugins/
```

Official plugins are normal plugin packages. Build and release outputs carry
official plugins as bundled seed content under the application install
directory:

```text
bundled-plugins/
```

On startup, Core synchronizes bundled official plugin packages into the runtime
plugin root. Runtime discovery scans the runtime plugin root, not the
application install directory.

This is a pre-stable breaking directory relocation. The old
`AppContext.BaseDirectory/plugins` runtime layout is not kept as a compatibility
read path.

Each plugin embeds:

```text
ComCross.Plugin.Manifest.json
```

The manifest declares plugin id, version, entry point, plugin type, permissions, optional settings pages, and optional i18n resources.

## Host Model

- `PluginHost` loads plugin assemblies and handles plugin-level IPC.
- `SessionHost` handles session-scoped host behavior.
- `ExtensionHost` handles extension processes.
- Core owns host lifecycle coordination.
- Shell consumes Shell/Core-facing services instead of raw host protocol messages.

## Capability Model

Bus plugins expose capabilities through `IPluginCapabilityProvider`.

Capability descriptors can include:

- Stable capability id.
- Localized name/description keys.
- JSON schema and UI schema for connection parameters.
- Default parameters.
- Shared-memory request.
- Session host model.
- Optional `PluginConnectionResourceDescriptor`.

`PluginConnectionResourceDescriptor` lets a plugin declare that one committed parameter represents an exclusive local resource, such as a serial `port`. Shell/Core may use this public descriptor to show a generic conflict prompt, but the plugin remains the authority for final connect success or failure.

## Producer/Consumer Boundary

For bus plugins, plugin-produced facts include:

- Connection parameter schemas.
- Connection result metadata.
- Session title, subtitle, icon, reconnect policy, and topology.
- Managed resource kinds and resource actions.
- Plugin UI state.
- Transmit targets.
- Startup session-state patches.
- Message frame attributes.

Core/Shell must not infer these facts from plugin-private parameter names or implementation details.

## UI State And Settings

Plugins can implement `IPluginUiStateProvider`.

Core queries UI state with `PluginUiStateQuery`. The query may include:

- `CapabilityId`
- `SessionId`
- `ViewKind`
- `ViewInstanceId`
- `ResourceKind`
- `ResourceId`
- read-only `Settings`

Plugin settings pages are declared in the manifest. Core persists settings and passes a settings snapshot into UI-state queries. Snapshot keys use:

```text
{settingsPageId}.{fieldKey}
```

Example: the serial adapter declares serial scan patterns as plugin settings. The serial plugin applies those settings when producing its `ports` UI state. Shell renders the returned state and dispatches the plugin refresh action; Shell/Core do not scan serial devices directly.

## Session Metadata

Connect results and startup initialization can provide host-visible metadata:

- `DisplayTitle`
- `DisplaySubtitle`
- `SessionIcon`
- `CanReconnect`
- `ParentSessionId`
- `ManagedResourceKinds`

Core stores this metadata in the session descriptor. Shell renders it directly.

TCP listener accepted clients are scoped child sessions because accepted TCP connections have independent connection lifecycles. UDP listener endpoints are not child sessions; they are transmit targets and message attributes.

## Message Frame Attributes

Message frames support bounded attributes for facts that belong to a single frame.

Limits:

- schema version: `1`
- max entries: `8`
- key: lowercase ASCII letters, digits, `.`, `_`, `-`; UTF-8 byte length <= `32`
- value: non-null string; UTF-8 byte length <= `128`

Invalid attributes are dropped at the contract boundary. Values are not truncated. Attributes are sorted by key for stable storage, display, search, export, and extension delivery.

Use attributes for concise frame facts such as `source.endpoint`. Do not use attributes as payload storage, session metadata, or a general extension bag.

## Transmit Targets And Send Results

Some sessions can send to multiple plugin-defined targets. Plugins can implement `IPluginTransmitTargetProvider`.

The plugin returns:

- target id and display text
- optional subtitle
- optional default target
- optional last-seen time
- optional frame attributes
- whether a target is required before sending

Shell shows the target selector only when the plugin declares targets or requires a target.

`PluginSendCommand` carries an optional `TransmitTargetId`. `PluginCommandResult` is the authoritative send result and can include:

- success/failure
- error and error code
- bytes written
- target used
- target invalidation hint

For UDP listener replies, selected target attributes are copied onto the mirrored TX frame after send succeeds. UDP client RX frames do not add `source.endpoint`; the connected remote endpoint is already session-level information.
