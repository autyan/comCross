# UI/UX Specs (MVP)

Version: 0.1

## 1) Product Shape
- Workspace-first, message stream is the primary surface.
- Tools are peripheral and modular.

## 2) Information Architecture
Primary areas:
1) Top Bar
2) Left Sidebar (Devices/Sessions)
3) Center Workspace (Message Stream)
4) Right Tool Dock (Tools)
5) Bottom Status Bar

Navigation rules:
- Workspace is always visible.
- Tools never replace the workspace.
- Per-session context is always visible.

## 3) Layout & Grid
- Base grid: 12 columns
- Left Sidebar: 2-3 columns
- Center Workspace: 7-8 columns
- Right Tool Dock: 2-3 columns (collapsible)
- Spacing: 4 / 8 / 12 / 16 / 24
- Panel radius: 6px
- Input radius: 4px

Breakpoints:
- Desktop >= 1280px: full layout
- Tablet 960-1279px: tool dock collapses by default

## 4) Visual Style

### Typography
- Titles/Headings: IBM Plex Sans
- Body/Labels: IBM Plex Sans
- Logs/Code: JetBrains Mono

Type scale:
- H1: 20
- H2: 16
- H3: 14
- Body: 13
- Caption: 12
- Log line: 13 (mono)

Line height:
- Body: 1.35-1.4
- Log: 1.45

### Color Tokens
```
--bg-0: #0F1417
--bg-1: #141B1F
--bg-2: #1B242A

--text-0: #E6EDF3
--text-1: #B7C1CC
--text-2: #87909B

--accent: #2CB5A9
--accent-2: #3AA0FF
--success: #2CB5A9
--warn: #F5A524
--error: #E5534B

--border: #25303A
--shadow: rgba(0,0,0,0.3)
```

Usage:
- Workspace background: bg-0 to bg-1 subtle gradient
- Panels: bg-1/bg-2 with border
- Tool dock: bg-2
- Active state: accent-2
- Connected: success

### Elevation
- Panels: 0 4px 12px shadow
- Floating tools: 0 8px 20px shadow

## 5) Component Specs

### Top Bar
- App title
- Active workspace name
- Session status
- Quick actions: Connect, Disconnect, Clear, Export

### Left Sidebar
- Device list
- Favorites
- Session list

States:
- Active session highlighted (accent-2 bar)
- Connected sessions show green dot

### Center Workspace
- Log view (monospace)
- Search bar at top of stream
- Filter bar under search
- Metrics strip: RX/TX, rate, elapsed time

Log styling:
- Timestamp, source tag, content
- Highlight: left color bar + subtle background
- Error line: error color text + left bar

### Right Tool Dock
- Tool tabs in vertical bar
- Tool panel width 320px default
- Tools: Send, Filter, Highlight, Export

### Bottom Status Bar
- Global status: CPU/memory, auto-save state
- Notification area: alerts

## 6) UX Behavior
- Connect opens new session tab.
- Each session has independent settings.
- Search is incremental.
- Filters are keyword or regex.
- Filter applies to view only.
- Workspace auto-save on exit.

## 7) Tool System UX
- Tools share workspace context.
- Tool switching is instant.
- Disabled tool tab is hidden.

## 8) Animation
- Tool panel expand/collapse: 150-200ms ease
- No animation for log streaming.

## 9) Accessibility
- Contrast ratio > 4.5:1
- Keyboard accessible actions
- Focus visible with accent border

## 10) MVP Screens
1) Main Workspace
2) Session Connect Dialog
3) Tool: Send
4) Tool: Filter/Highlight
5) Tool: Export
6) Workspace Manager

## 11) Figma Notes
- Build styles: Colors, Typography, Effects
- Reusable components: Tabs, Panels, Buttons, Inputs
- Ensure layout works at 1280x720 and 1440x900
