# UI/UX Specification

Version: v0.4

## 1) Product Shape

- Workspace-first communication tool.
- Message stream is the primary surface.
- Connection creation and session management are always close to the workspace.
- The right panel supports sending and quick commands without replacing the message stream.
- Plugin-provided facts drive bus-specific UI content.

## 2) Information Architecture

Primary areas:

1. Top bar and workload tabs.
2. Left rail for primary mode switching.
3. Left sidebar for quick-create and session list.
4. Center workspace for active session message stream.
5. Right panel for send controls and quick commands.
6. Bottom status bar.

Navigation rules:

- Workspace remains visible while sessions are active.
- Session context is visible in the center header.
- Plugin-specific connection details are rendered through the common connection UI path.
- Shell does not expose plugin-private parameters as separate one-off UI flows.

## 3) Layout

- Desktop layout targets 1280px and wider.
- Left rail stays compact and icon-based.
- Left sidebar is dense enough for repeated session switching.
- Center workspace uses most of the horizontal space.
- Right panel remains narrow and action-oriented.

Spacing tokens:

- 4 / 8 / 12 / 16 / 24

Shape:

- Cards and panels use restrained radius, normally 6-8px.
- Icon buttons must have a stable hit target, not just an icon-sized content region.

## 4) Visual Style

### Typography

- Titles/headings: IBM Plex Sans or platform fallback.
- Body/labels: IBM Plex Sans or platform fallback.
- Message/code text: JetBrains Mono or monospace fallback.

Type scale:

- H1: 20
- H2: 16
- H3: 14
- Body: 13
- Caption: 12
- Message line: 13 mono

### Color Tokens

```text
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
```

Usage:

- Active state uses `accent-2`.
- Connected state uses `success`.
- Warning and error states must be visually distinct from active selection.

## 5) Components

### Left Sidebar

- Quick-create view renders plugin capability schemas and plugin UI state.
- Session list renders plugin-provided title, subtitle, icon, topology, and reconnect policy.
- Connection parameter entry points use icon buttons with tooltips and stable hit targets.

### Center Workspace

- Header shows active session title, status, endpoint/subtitle, and session actions.
- Search is incremental and applies to message text and searchable attributes.
- Message stream shows direction, timestamp, content, and compact attributes.
- The search input should be styled as one integrated control, not nested visible input frames.

### Right Send Panel

- Message input supports STR mode, CR/LF options, clear-after-send, and send-result errors.
- Target selector appears only for sessions whose plugin declares transmit targets or requires a target.
- Quick commands show up to 8 pinned commands; defaults initialize 3 pinned commands.
- Quick command search opens a lightweight candidate popup only while searching.
- Pinned command rows include send and edit actions.

### Dialogs And Popups

- Destructive actions use localized confirmation dialogs.
- Popup candidate lists close on Escape, outside click, and completed selection.
- Lightweight editors should not block unrelated workspace context unless the action is destructive.

## 6) UX Behavior

- Creating a session saves a descriptor and selects it.
- Reconnect is shown only when plugin metadata allows it.
- Deleting a session is destructive and removes persisted session data.
- Send failures are surfaced where the user initiated the send.
- Plugin UI state refreshes should be visible as disabled/loading state when user action would otherwise appear ignored.

## 7) Accessibility

- Contrast ratio should be at least 4.5:1 for text.
- Keyboard focus must be visible.
- Icon-only buttons require tooltips or accessible labels.
- Clickable visual affordance and actual hit target must match.

## 8) Release Screens

1. Main workspace with active session.
2. Quick-create panel.
3. Session list with parent/child topology.
4. Session detail and connection parameters.
5. Right send panel with quick commands.
6. Settings with plugin settings pages.
