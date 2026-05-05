# Session Storage UI Design

## Purpose

This document defines the v0.6.0 UI shape for Session Logs, Session Archive,
message viewing, storage status, and complete data export.

The UI goal is to expose stable product entry points for the target storage
model while keeping MVP implementation depth narrow. The first version should
make storage behavior visible, actionable, and predictable without turning the
release into a full archive manager, benchmark platform, decoder workflow, or
analysis suite.

## Scope

In scope:

- global Session Logs settings for the live spool;
- per-session Archive management;
- live and history source switching in the message viewer;
- Plain, Slim, and Detailed message display density;
- independent STR and HEX payload rendering;
- complete available-data export from Live Spool or Archive;
- storage calibration, pressure, loss, archive failure, and abnormal data
  messages;
- session deletion copy that states Archive deletion impact.

Out of scope:

- custom protocol decoding and `Decode Me`;
- archive import, replay, backfill, synchronization, or migration UI;
- selected-frame, visible-window, or range export;
- persistent search indexes, search result explanation, or advanced query UI;
- user-facing benchmark reports or storage tuning controls;
- direct spool file browsing or direct SQLite archive editing inside ComCross.

## Design Principles

- Storage UI should be operational and compact. It should support repeated use
  during long-running capture sessions rather than presenting a marketing-style
  overview.
- Session Logs and Application Logs must be named separately. Session Logs are
  communication data backed by the live spool. Application Logs are program
  diagnostics.
- Archive is a session-level explicit action. It must not appear as a global
  default database persistence toggle.
- Message data source, display density, and payload rendering are independent
  choices.
- Loss, cleanup, storage pressure, archive write failure, and abnormal local
  data must not be silent.
- Shell requests storage actions through Core-facing services. Shell must not
  read spool files or archive databases directly.

## UI Surfaces

### Settings

The current settings page should split communication data storage from program
diagnostics.

`Application Logs`

- Controls runtime diagnostic logging.
- Owns application log directory, format, and minimum level.
- Does not describe communication frame retention.

`Session Logs`

- Controls live spool behavior.
- Shows the active Session Logs directory.
- Provides an `Open Data Directory` action.
- Configures global maximum Session Logs size.
- Configures per-session maximum Session Logs size.
- May expose segment size only if implementation needs user control in this
  release. Otherwise segment size remains an internal default.
- Shows calibration status.
- Shows storage health.
- Does not provide an Archive or SQLite global enable switch.
- Does not promise long-term retention.

Storage settings copy should make the business model explicit:

- Session Logs are bounded communication logs.
- Session Logs are optimized for live browsing, short-term export, and
  real-time analysis input.
- Session Logs may be cleaned when limits are reached.
- Long-term history requires enabling Archive for a specific session.

The UI should also prepare a clear user-facing entry for high-volume capture
expectations. The MVP does not need a benchmark report or tuning console, but
users should be able to understand:

- the active calibration phase and storage policy;
- whether ComCross is using conservative, limited, normal, or high-capacity
  behavior;
- that ComCross is optimized for storage-first communication capture and may
  degrade viewer, archive, export, or analysis work before compromising live
  spool append;
- that v1.0 does not promise general-purpose line-rate packet capture or logic
  analyzer style sampling;
- where to find Session Logs data and how to export complete available landed
  data.

This entry may start as compact Settings copy and storage status. It should
remain compatible with a future first-run or help-panel explanation if product
validation shows that high-volume capture positioning needs a more explicit
surface.

### Session Detail Drawer

The session detail drawer is the primary Archive management entry point because
Archive is session-scoped.

The drawer should add a compact `Archive` section with:

- Archive state: `Disabled`, `Enabled`, or `Error`;
- stored size when known;
- latest archive error summary when state is `Error`;
- `Enable Archive` action when disabled;
- `Stop Writing` action when enabled;
- `Delete Archive Data` action when archive data exists;
- a disabled or unavailable state when the session cannot manage Archive.

Enabling Archive requires a second confirmation dialog. The confirmation must
state:

- only frames received or sent after enablement are archived;
- existing live spool data is not backfilled;
- enabling Archive does not change Session Logs or spool cleanup behavior;
- Archive may use additional disk and I/O;
- disabling Archive later stops future writes but keeps existing data.

Stopping Archive writing does not delete existing archive data.

Deleting Archive data is a separate destructive action and requires
confirmation.

### Message Viewer

The message viewer should expose three independent controls.

`Data Source`

- `Live`: reads the live spool.
- `History`: reads the session Archive.

`History` is disabled when Archive is disabled or unavailable. Disabled state
should use a concise tooltip or inline state label, not a modal dialog.

`Display Density`

- `Plain`: payload only.
- `Slim`: direction marker plus payload.
- `Detailed`: frame metadata, attributes, direction, source, payload, and
  future action entry points.

`Payload Rendering`

- `STR`: render bytes as UTF-8 text with visible control-character markers.
- `HEX`: render bytes as hexadecimal bytes.

Payload rendering is independent from display density. `STR | HEX` must not be
merged into `Plain | Slim | Detailed`.

Recommended toolbar order:

```text
[ Live | History ]  [ Plain | Slim | Detailed ]  [ STR | HEX ]  Search  Export
```

The exact layout can adapt to available width, but the three choices should
remain conceptually separate.

#### Plain Mode

Plain mode behaves like plain text:

- raw payload only;
- no timestamp;
- no direction marker;
- no per-frame card;
- no extra vertical spacing between frames;
- monospace text;
- one frame per visual line or wrapped payload block according to viewer
  rendering rules.

#### Slim Mode

Slim mode remains compact and text-like:

- `RX` or `TX` marker;
- raw payload;
- no per-frame card;
- no extra vertical spacing between frames;
- monospace payload text.

#### Detailed Mode

Detailed mode is the richer operator view:

- frame ID;
- timestamp;
- direction;
- source;
- attributes;
- payload;
- future decode/action entry points when those features exist.

Detailed mode may use rows or compact panels, but it should not create a large
card-heavy feed. The viewer should remain suitable for scanning high-volume
streams.

#### Copy Behavior

Copy behavior should be as close as practical to what is rendered.

- Plain copy contains rendered payload lines.
- Slim copy contains direction marker plus rendered payload.
- Detailed copy may include metadata and attributes.
- Multi-frame Plain and Slim copy uses one newline between frames and no extra
  blank lines.
- Viewer truncation does not affect export.

#### Source State

The viewer must surface source states without interrupting normal capture.

Important states:

- no frames;
- no more frames before or after the current window;
- data was cleaned or evicted;
- Archive is disabled;
- Archive has an error;
- source is unavailable;
- abnormal local data was detected.

The default presentation should be a compact inline state row or low-noise
notification. It should not use repeated modal dialogs.

### Search

Search remains a lightweight message viewer aid in this storage UI scope.

- Search operates on the selected data source.
- Search returns matching frames or frame IDs to the viewer.
- Search does not explain results.
- Search does not build or expose a persistent index in this scope.
- Search does not include decoded protocol content in v0.6.0.

### Export

Export is available from the message viewer toolbar.

Export writes complete available data from one selected source:

- `Live Spool`;
- `Archive`.

The export UI should allow:

- source selection, defaulting to the current viewer source;
- export format selection: `Plain`, `Slim`, or `Detailed JSONL`;
- payload rendering selection for text-oriented exports;
- target file path with `.cclog` extension;
- progress and final result reporting.

Export must not offer selected-frame, visible-only, count-limited, or arbitrary
range export in v0.6.0.

Active Live Spool export captures the available frame range when the export
command is accepted. Frames landed after that point are not included. If cleanup
removes part of the captured source range during export, the UI reports a
partial export or source loss result.

Detailed JSONL export should preserve binary safety by using `payloadHex` and
including frame metadata and attributes. Detailed JSONL is not affected by the
viewer `STR | HEX` display toggle.

`.cclog` is an export artifact only. It is not an importable archive package in
this release.

### Notifications

Storage-related notifications should flow through the existing Notification
Center instead of introducing a separate storage inbox.

Required notification categories include:

- calibration completed and the active storage policy changed;
- storage path or hardware fingerprint changed and conservative policy is
  active until recalibration completes;
- spool write failure;
- storage pressure or degraded mode;
- data loss or dropped frames;
- cleanup removed older Session Logs data;
- Archive write failure;
- abnormal spool or Archive data detected;
- legacy SQLite message data detected.

Notifications must be throttled where a condition can occur repeatedly, such
as Archive write failure or spool write failure.

### Session Deletion

Deleting a session deletes its spool data.

If the session has Archive data, the delete confirmation must state that the
long-term Archive data will also be deleted. v0.6.0 does not present a checkbox
to retain or detach Archive data.

### Legacy Message Data

If old workspace SQLite message data is detected, Core should surface a message
to the user.

The UI should state that:

- the storage model has changed;
- old SQLite message data is not auto-migrated;
- ComCross does not provide import or migration UI in this release.

The UI may provide an `Open Data Directory` action. It should not provide an
in-app SQLite export workflow for the deprecated message table.

## Current UI Impact

The v0.6.0 storage UI affects these existing surfaces:

- `SettingsView`: rename and reshape log settings into Application Logs and
  Session Logs, remove global database persistence controls.
- `MessageStreamView`: add data source, display density, and payload rendering
  controls; adjust list templates for Plain, Slim, and Detailed modes; replace
  current session database checkbox.
- `MainWindow` session detail drawer: add session Archive management.
- `LeftSidebar` delete confirmation: include Archive deletion impact when
  applicable.
- `NotificationCenterView`: reuse existing notification list, adding storage
  message types and actions where needed.
- Export dialog/progress workflow: align export with `.cclog` complete-source
  contract.

## MVP Closure Review

### Over-Design Check

The UI scope avoids the largest expansion risks:

- no standalone Archive manager;
- no benchmark result page;
- no user-facing storage tier tuning;
- no archive import, replay, migration, or synchronization workflow;
- no decoder UI;
- no advanced search UI;
- no partial export workflow.

The remaining UI entry points are justified because they expose stable product
contracts that users need to understand: where live data is stored, when data
may be cleaned, how Archive is enabled, which source the viewer is reading, how
payload bytes are rendered, and what export contains.

### Closed Decisions

- Archive is managed per session, not globally.
- `STR | HEX` is independent from `Plain | Slim | Detailed`.
- Export is complete-source export only.
- `.cclog` is exported text, not a spool segment or archive package.
- Session deletion warns about Archive deletion but does not offer a retain
  checkbox in v0.6.0.
- Notifications are reused instead of adding a new storage message center.

### Open Implementation Details

These details can be decided during implementation without changing the
product contract:

- exact segmented-control styling and responsive toolbar layout;
- whether segment size is exposed in Settings or remains internal;
- exact Archive size calculation cadence;
- exact export progress presentation;
- exact inline state row wording for each query state;
- exact `STR` control-character marker syntax, as long as control characters
  are visually distinguishable from normal text.

### Remaining Risks

- Plain and Slim modes require a list rendering path that avoids per-frame card
  spacing while preserving virtualization and copy behavior.
- History source switching must avoid loading full Archive history into Shell
  memory.
- Archive state and storage health need Shell-facing models that do not leak
  spool file or SQLite implementation details.
- Export and viewer display share concepts but have different guarantees:
  viewer truncation is allowed, export truncation is not.
