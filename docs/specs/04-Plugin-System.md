# Plugin System (MVP)

## Goals
- Tools are independent modules loaded from DLLs.
- Users can install tool groups during setup or add offline.
- Tools do not replace the workspace; they dock around it.

## Plugin Layout
- /plugins/<tool-id>/
  - tool.json (manifest)
  - tool.dll
  - assets/

## Manifest (Concept)
```
{
  "id": "serial.send",
  "name": "Send Panel",
  "version": "1.0.0",
  "targetCoreVersion": "0.1",
  "entryPoint": "ComCross.Tools.Send.SendTool",
  "toolGroup": "serial",
  "permissions": ["serial.write", "workspace.read"],
  "ui": {
    "dock": ["right", "float"],
    "minWidth": 280,
    "defaultOpen": true
  }
}
```

## Loading Rules
- Scan /plugins directory on startup.
- Validate manifest and version compatibility.
- Load tool metadata; create UI only when activated.
- Disabled tools are hidden and not activated.

## Tool Lifecycle
1) Discover
2) Validate
3) Register
4) Activate (when user opens)
5) Deactivate (on close)

## Security (MVP)
- Manifest-based permission display.
- Optional hash allowlist for offline tools.

## Tool Manager UI
- Show installed tools and status (enabled/disabled).
- Enable/disable with immediate effect.
