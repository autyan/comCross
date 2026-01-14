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

## 6) Permissions

Permissions are declared in the manifest and are used to inform users what
the plugin is allowed to do. These do not enforce security at runtime yet.

## 7) Notifications (Optional)

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

## 8) Packaging

Place the compiled plugin DLL under its folder in `plugins/`:

```
plugins/
  serial.stats/
    tool.dll
```

## 9) Notes

- Keep plugins isolated from core services unless explicitly supported.
- Do not depend on internal UI types that may change between versions.
