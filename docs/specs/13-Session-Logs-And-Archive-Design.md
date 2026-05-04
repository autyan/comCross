# Session Logs And Archive Design

## Purpose

This document defines the v0.6.0 message storage direction for ComCross.
The goal is to make high-volume session messages streamable, bounded, and
observable before adding heavier decoder, protocol, replay, or long-term
analysis features.

The product architecture in this document is intentional and should remain
stable. Some lower-level implementation details are marked as MVP guidance so
the first implementation can stay pragmatic and evolve after validation.

## MVP Principle

The MVP should expose the target product entry points and preserve stable
contracts for storage source, frame identity, archive lifecycle, viewer modes,
and observable loss. The implementation may use conservative file formats,
minimal indexes, lightweight storage calibration, and limited query
capabilities as long as those contracts remain stable.

In practical terms, v0.6.0 should deliver the user-facing shape of the target
storage product without requiring the final storage engine, final file format,
full indexing strategy, replay system, portable archive import/export workflow,
or decoder pipeline.

## Product Contract

### User-Facing Terms

- `Application Logs`: application runtime diagnostics, errors, and operational
  logs. These are not communication data.
- `Session Logs`: communication message logs for a session. In v0.6.0 these are
  backed by a file-based session spool. They support live browsing, short-term
  export, and live analysis input, but they are bounded and may be cleaned.
- `Session Archive`: optional session-level long-term storage. Users must enable
  it manually for a specific session and confirm the trade-off. It is backed by
  SQLite and is intended for history browsing.

The current user concept of "logs" maps to `Session Logs`, not to application
runtime logs.

### Storage Layers

ComCross message data is split into three storage layers.

`App And Workspace State`

- Contains configuration, notifications, lightweight workspace/session
  descriptors, plugin UI state, and other low-volume state.
- Must not store high-volume raw message data.

`Session Spool`

- Always available for active session message flow.
- Accepts RX/TX frames from Core ingest.
- Backs Session Logs with file-based storage.
- Supports live and recent-history browsing.
- Supports short-term export from currently available spool data.
- Provides real-time analysis input without keeping all frames in Core memory.
- Is a working buffer, not a long-term archive.
- Is governed by global and per-session size limits.
- May clean old data according to retention policy.

`Session Archive`

- Optional and session-scoped.
- Disabled by default.
- Cannot be enabled globally by default.
- Requires explicit user action and second confirmation.
- Stores only frames received or sent after archive is enabled.
- Keeps archived frames independent of spool cleanup.
- Provides history data mode where the message viewer reads from archive.
- Disabling archive stops future writes but keeps existing archive data.
- Archive deletion is a separate confirmed operation.

### Product Entry Points

The MVP should include stable entry points for the target product flow:

- Session Logs settings and storage status.
- Per-session Archive management.
- Archive enable, disable, and delete actions.
- Live and History data source switching in the message viewer.
- Plain, Slim, and Detailed message display modes.
- Storage calibration, pressure, loss, cleanup, and archive-failure messages.
- Basic export for complete available data from the selected message data
  source.
- Abnormal local data messages when startup or browsing is affected.

These entry points may expose limited MVP behavior, but their product meaning
should not be temporary.

### Non-Negotiable Boundaries

- Enabling archive must not change spool file structure, spool cleanup, or spool
  read behavior.
- Spool and archive are independent consumers of the same Core-ingested frames.
- Core ingest assigns the session-scoped frame ID once. Spool, archive, live
  notifications, and future analysis/decoder consumers reuse that same ID.
- Archive write failure must not block or fail spool writes.
- Archive write failure must be user-visible, but notifications must be
  throttled and must not be emitted once per frame.
- Data loss, cleanup loss, storage overload, and archive failure must never be
  silent.
- Bus plugins remain producers of transport facts. Storage, archive, analysis,
  and future decoding must not change transport plugin responsibilities.

### Message Viewer Contract

The message viewer has independent data source and display mode selections.

Data sources:

- `LiveSpool`: reads the active session spool.
- `Archive`: reads the session archive in history data mode.

Display modes:

- `Plain`: raw data only.
- `Slim`: RX/TX marker plus raw data.
- `Detailed`: metadata, attributes, actions, and future decode entry points.

Plain and Slim modes should visually behave like plain text:

- no card frame per message;
- no extra vertical spacing between frames;
- monospace text;
- copy should be as close as practical to what is rendered;
- copied multi-frame content should use one newline between frames and no extra
  blank lines.

### Export Contract

v0.6.0 export is intentionally narrow. Export writes the complete available
data from one message data source:

- `LiveSpool`: export the complete data that is still available in the session
  spool.
- `Archive`: export the complete data available in the session archive.

Spool files use an internal binary working format. Export must materialize the
selected source into an external plain-text file format instead of exposing
spool segment files as user-facing artifacts.

Export modes mirror the message viewer display modes:

- `Plain`: raw data only.
- `Slim`: RX/TX marker plus raw data.
- `Detailed`: frame metadata plus raw data. Detailed export must include frame
  ID, timestamp, direction, source, and attributes so buses such as UDP can
  preserve necessary endpoint/source facts.

The export writer must stream from the selected source instead of requiring all
frames to be loaded into Shell memory. The exact text syntax is an MVP
implementation detail, but it must be deterministic and safe for binary
payloads.

Export does not define a portable archive package and does not import data back
into ComCross. Exporting selected frames, visible-only windows, partial query
ranges, replay packages, compressed archives, or protocol-decoded results is out
of scope for this storage MVP.

### Session Deletion

Deleting a session deletes its spool data.

If the session has archive data, the delete confirmation must state that
long-term archive data will also be deleted. The archive deletion option is
selected by default if the UI presents it as a separate option. v0.6.0 is not
required to support retaining orphan archives after session deletion.

## MVP Implementation Requirements

The MVP must satisfy the product contract above while leaving implementation
room for future hardening.

Search remains part of the broader v0.6.0 roadmap, but it is not part of this
first storage MVP acceptance unless explicitly implemented in a later scope.

### Storage Location

The MVP must separate app/workspace data, session spool data, and session
archive data under the local data directory. Stable internal IDs must be used
for storage identity; user-facing names are mutable and must not be used as
filesystem identity.

Recommended layout:

```text
<LocalDataDirectory>/
  data/
    app.db
    workspace.db

  session-spool/
    <workspace-id>/
      <session-id>/

  session-archive/
    <workspace-id>/
      <session-id>/
```

The exact file names and subdirectories are implementation details for v0.6.0,
as long as the layer separation and cleanup behavior are preserved.

### Session Spool

The MVP spool must be file-backed and append-oriented. It must be able to:

- append frames without keeping all historical frames in memory;
- read windows of frames by stable frame ID or latest range;
- track available history so evicted data can be reported;
- enforce global maximum size;
- enforce per-session maximum size;
- clean old data without deleting the active write target;
- expose cleanup/loss state to Core and Shell.

The MVP may choose any simple recoverable file format. A custom segment format,
length-prefixed records, or another append-friendly representation are all
acceptable if they preserve frame metadata and raw payload.

An auxiliary index is optional for MVP. If present, it must be rebuildable and
must not become the source of truth.

The spool format is an internal working format. It is not the user-facing export
format and does not need to be readable as plain text.

### Session Archive

The MVP archive must be a separate session-level SQLite store. It must:

- be enabled per session only;
- write only frames after enablement;
- store enough data to browse history without depending on spool;
- support basic window queries by frame ID and latest range;
- support complete available-data export through the common export path;
- preserve the Core-assigned frame ID;
- keep existing archive data when disabled;
- report write failure without blocking spool writes.

The initial SQLite schema is an implementation detail. It must be minimal and
focused on history browsing. It should not introduce decoder, replay, analysis,
or import/export schema unless that functionality is actually implemented.

### Storage Calibration And Backpressure

The MVP must include storage calibration and conservative fallback. This is not
a benchmark platform and does not need to expose scores, reports, or tunable
benchmark suites to users.

Required behavior:

- Before the first valid calibration completes, storage uses a conservative
  policy.
- If the storage environment fingerprint changes, the policy returns to
  conservative and calibration is rerun.
- Calibration must estimate the current machine/path capacity for the active
  storage path, especially spool write/read capacity.
- Archive capacity must be measured before or when archive is enabled, or the
  archive writer must use a conservative policy until measured.
- Storage overload and loss must be visible through diagnostics and user
  notifications.
- Storage pressure must integrate with existing bus pressure handling.

The exact calibration workload, threshold values, fingerprint inputs, and policy
names are MVP implementation details. The fingerprint must avoid raw private
hardware identifiers.

### Abnormal Data Handling

The MVP does not support archive import or migration. Core still must detect
and report abnormal local data that affects startup, browsing, cleanup, or
archive writes.

Required MVP categories:

- spool manifest or metadata cannot be read;
- spool data cannot be read for the requested session;
- cleanup fails;
- archive database cannot be opened;
- archive schema is unsupported;
- archive/session identity does not match.

Recoverable issues may be repaired or isolated automatically. Non-recoverable
issues must not silently break app startup or message browsing.

## Initial Implementation Guidance

The following details are recommended starting points, not stable storage
contracts. They may change during implementation if the MVP requirements remain
satisfied.

### Spool Files

An append-only segment model is a good initial fit:

- one active segment per session;
- sealed segments become cleanup candidates;
- records include frame ID, timestamp, direction, format, source, attributes,
  payload length, and payload bytes;
- records are length-prefixed so scanning can recover after partial writes;
- basic per-record integrity checks may be added if inexpensive.

The segment format should favor write throughput, bounded cleanup, and windowed
reads over direct human readability. Human-readable session data leaves the
system through the export path.

Suggested file naming:

```text
session-spool/<workspace-id>/<session-id>/
  manifest.json
  segments/
    0000000000000001.csf
    0000000000000002.csf
```

A sparse index file can be added later if windowed reads need faster random
access. MVP should not require a separate index to prove the model.

### Archive SQLite

A minimal archive database can start with an info table and a frame table:

```sql
CREATE TABLE archive_info (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE frames (
    id INTEGER PRIMARY KEY,
    timestamp_utc_ms INTEGER NOT NULL,
    direction TEXT NOT NULL,
    format TEXT NOT NULL,
    source TEXT NOT NULL,
    raw_data BLOB NOT NULL,
    byte_count INTEGER NOT NULL,
    attributes_json TEXT NOT NULL,
    attribute_schema_version INTEGER NOT NULL,
    created_at_utc_ms INTEGER NOT NULL
);

CREATE INDEX idx_frames_time ON frames(timestamp_utc_ms);
CREATE INDEX idx_frames_direction_id ON frames(direction, id);
```

This schema should be treated as a practical starting point. It is not a
promise to support archive import/export or long-term external compatibility in
v0.6.0.

### Cleanup Ordering

Recommended cleanup order:

1. Do not delete the active segment.
2. Prefer old sealed segments from inactive sessions.
3. Then delete old sealed segments from non-current sessions.
4. Then delete old sealed segments from the current session if required.
5. Update available-history metadata.
6. Notify when cleanup affects currently visible history.

The exact scoring algorithm can evolve as long as cleanup remains bounded and
observable.

## Deferred Hardening

The following items are intentionally not part of the first MVP closure unless
implementation discovers they are required for correctness:

- stable public spool binary format;
- mandatory per-record CRC;
- mandatory rebuildable spool index;
- archive backfill from spool;
- archive synchronization for active or inactive sessions;
- archive replay;
- portable archive import/export package;
- partial range export;
- archive compaction or compression UI;
- orphan archive retention after session deletion;
- payload full-text indexing;
- decoded-field persistence or search;
- CPDL profile integration;
- custom protocol decoder implementation.
