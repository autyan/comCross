# Workspace State

## Overview
Workspace state is split into two layers:
- Toolset: tool selection and layout.
- WorkState: active sessions and UI state.

## Toolset (Tool Combination)
- Enabled tools
- Dock layout
- Tool defaults

## WorkState (Working State)
- Sessions and port settings
- UI state (filters, highlights, scroll)
- Send history

## JSON Example
```
{
  "toolset": {
    "id": "default",
    "name": "Default Toolset",
    "enabledTools": ["serial.send", "serial.filter"],
    "layout": {
      "panels": [
        {"toolId": "serial.send", "dock": "right", "width": 320, "isOpen": true},
        {"toolId": "serial.filter", "dock": "right", "width": 320, "isOpen": false}
      ]
    },
    "toolSettings": {
      "serial.send": {"defaultMode": "STR", "appendNewline": true}
    }
  },
  "workState": {
    "workspaceId": "last",
    "sessions": [
      {
        "id": "ttyUSB0",
        "port": "/dev/ttyUSB0",
        "settings": {
          "baud": 115200,
          "dataBits": 8,
          "parity": "None",
          "stopBits": 1
        },
        "connected": true,
        "metrics": {"rx": 12345, "tx": 678}
      }
    ],
    "uiState": {
      "activeSessionId": "ttyUSB0",
      "autoScroll": true,
      "filters": {"keyword": "ERROR", "regex": null},
      "highlightRules": ["default"]
    },
    "sendHistory": ["AT+RST", "AT+GMR"]
  }
}
```
