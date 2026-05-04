# Session Storage Technical Design

## Purpose

This document turns the v0.6.0 Session Logs and Archive product contract into
MVP technical decisions. It defines the first implementation shape for frame
contracts, spool storage, archive storage, query semantics, export, calibration,
and degradation.

The goal is not to build a storage platform. The goal is to make the
file-backed Session Logs path reliable on older machines and slow disks while
releasing safe capacity on newer hardware through fixed calibration tiers.

## Scope

In scope:

- versioned frame record contract;
- session-scoped frame ID allocation;
- file-backed live spool;
- segment-size rollover and segment-level cleanup;
- unified query service for live spool and archive;
- per-session SQLite archive writer and history query;
- complete available-data export to `.cclog`;
- lightweight storage calibration and health state;
- legacy SQLite message table deprecation.

Out of scope:

- search indexes or persistent search results;
- decoder and CPDL integration;
- archive replay;
- archive import/export packages;
- archive backfill or synchronization;
- public stable spool binary format;
- complex adaptive performance tuning.

## MVP Review Guardrails

The technical design intentionally keeps the target contracts larger than the
first implementation depth. The first implementation must avoid these expansion
traps:

- Do not turn calibration into a benchmark platform.
- Do not add user-facing tuning controls for storage tiers.
- Do not build persistent search indexes in this storage scope.
- Do not make `.cclog` an importable archive package.
- Do not make the spool binary format public or externally stable.
- Do not make archive availability part of the core ingest success path.
- Do not keep all historical frames in Shell or Core memory to simplify the
  viewer.

The closure criteria are: spool-first data landing works, available data can be
queried and exported, pressure/loss is visible, archive remains optional, and
future decoder/analysis consumers have a stable frame contract.

## Core Contracts

### MessageFrameRecord

`MessageFrameRecord` is the canonical frame fact consumed by Shell, export,
analysis, and future decoder entry points.

It should remain in a lightweight shared contract assembly so future analysis
plugins can reference it directly. Consumers may also implement compatible
readers based on the schema version.

Fields:

```text
SchemaVersion: int
FrameId: long
SessionId: string
TimestampUtc: DateTime
Direction: FrameDirection
RawData: byte[]
Format: MessageFormat
Source: string
Attributes: IReadOnlyDictionary<string, string>
AttributeSchemaVersion: int
```

Rules:

- `FrameId` is unique only inside one session.
- `FrameId` is a signed 64-bit integer.
- `FrameId` starts at `1` for each session.
- `SchemaVersion` versions the frame record contract.
- `AttributeSchemaVersion` versions the attributes contract.
- `RawData` is always the original payload bytes, not rendered text.

### Frame ID Allocation

Core ingest owns frame ID allocation. Storage implementations do not allocate
their own frame IDs.

Recommended service:

```text
SessionFrameIdAllocator
  Next(sessionId) -> long
  Restore(sessionId, nextFrameId)
```

Startup restore uses the highest known frame ID from the live spool metadata,
archive metadata if available, and any session runtime state that remains
authoritative. The next frame ID is `maxKnownFrameId + 1`.

## Write Pipeline

The write path prioritizes spool durability over every optional consumer.

```text
Frame ingest
  -> assign frame ID
  -> write live spool
  -> notify live consumers
  -> enqueue archive write if archive is enabled
```

Rules:

- Spool write is the core path.
- Archive write is asynchronous extension behavior.
- Archive failure must not fail or delay spool writes.
- Viewer refresh, export, and analysis must not block spool append.

## Storage Interfaces

The current in-memory `IFrameStore` model should be replaced for the v0.6.0
storage path. It can remain as fallback or test infrastructure.

Recommended services:

```text
ISessionFrameIngestService
ISessionSpoolStore
ISessionArchiveStore
IMessageFrameQueryService
ISessionLogExportService
IStorageCalibrationService
IStorageHealthService
```

Shell should use Shell-facing services or `IMessageFrameQueryService` through a
Core-facing adapter. It should not read spool files or archive databases
directly.

`IFrameStore` and `InMemoryFrameStore` may remain as fallback or tests, but they
must not remain the default Session Logs fact source after the spool path is in
place.

## Live Spool

### Format

The MVP live spool uses an internal append-oriented binary working format. The
format is not a public or user-facing export format.

The first implementation should use segment files with length-prefixed records.

Minimum segment metadata:

```text
magic
schemaVersion
segmentId
sessionId
createdAtUtc
```

Minimum record data:

```text
recordLength
frameId
timestampUtc
direction
format
source
attributeSchemaVersion
attributesJsonLength
payloadLength
attributesJson
payloadBytes
```

Optional MVP fields:

- record flags;
- per-record checksum;
- per-segment checksum.

Checksums are not required for the first MVP closure. The reader must still be
able to detect an incomplete record through `recordLength` and payload bounds.

### Segment Rollover

Rollover is based only on segment file size in the MVP.

Configurable values:

- global spool maximum size;
- per-session spool maximum size;
- segment maximum size.

Time-based rollover, frame-count rollover, compression, and hot/cold tiering are
out of scope.

### Manifest

The manifest is operational metadata, not the raw data source.

Recommended fields:

```json
{
  "schemaVersion": 1,
  "workspaceId": "...",
  "sessionId": "...",
  "activeSegmentId": 3,
  "firstAvailableFrameId": 1,
  "lastFrameId": 12000,
  "segmentMaxBytes": 67108864,
  "totalSpoolBytes": 123456789,
  "segments": [
    {
      "segmentId": 1,
      "fileName": "0000000000000001.csf",
      "firstFrameId": 1,
      "lastFrameId": 5000,
      "byteCount": 67100000,
      "sealed": true
    }
  ]
}
```

Update strategy:

- Do not update manifest for every frame.
- Update on rollover, cleanup, shutdown flush, and periodic checkpoint.
- On startup, repair active-segment metadata by scanning the active segment when
  manifest state is stale.

### Recovery

MVP recovery behavior:

- If manifest is missing but segment files exist, scan segments and rebuild a
  minimal manifest when possible.
- If an active segment ends with an incomplete record, ignore or truncate the
  incomplete tail.
- If a sealed segment cannot be read, isolate it and report abnormal data.
- If metadata cannot be repaired, mark the source unavailable and notify the
  user.

### Index

A persistent index is not required in the MVP.

Index purpose when added later:

- faster frame ID to segment offset lookup;
- faster `Before(frameId)` queries;
- faster random access inside large segments.

MVP lookup strategy:

- use manifest segment `firstFrameId` and `lastFrameId` to choose segment;
- scan within the segment;
- optionally build an in-memory sparse index during runtime.

### Cleanup

Cleanup removes sealed segments only. It never removes the active write target.

Recommended cleanup order:

1. Old sealed segments from inactive sessions.
2. Old sealed segments from non-current sessions.
3. Old sealed segments from the current session if required.

Cleanup updates available-history metadata. If cleanup affects visible history,
Core surfaces a user-visible event.

## Archive Store

### Lifecycle

Archive state is part of the session descriptor.

MVP states:

```text
Disabled
Enabled
Error
```

Rules:

- Enable starts writing future frames only.
- Enable does not backfill live spool data.
- Disable stops future writes but keeps existing archive data.
- Delete archive data is a separate confirmed action.
- Session deletion warns that archive data will also be deleted. The MVP is not
  required to support retaining orphan archives.

### Writer

Archive writer is asynchronous and bounded.

Rules:

- Frames are enqueued only after successful spool write.
- Archive queue overflow or write failure does not affect spool.
- Failures set archive state to `Error`.
- Notifications are throttled per session and error kind.
- State transition from healthy to error notifies immediately.
- Repeated same-kind errors should notify at most once per configured interval,
  for example five minutes.

### SQLite Schema

Initial schema:

```sql
CREATE TABLE archive_info (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE frames (
    frame_id INTEGER PRIMARY KEY,
    session_id TEXT NOT NULL,
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
CREATE INDEX idx_frames_direction_id ON frames(direction, frame_id);
```

`archive_info` minimum keys:

```text
schema_version
workspace_id
session_id
created_at_utc
app_version_created
```

`session_id` remains in the frame table even though archive is session-scoped.
It supports diagnostics and identity validation.

## Query Service

### Data Sources

```text
LiveSpool
Archive
```

### Query Types

```text
Latest(limit)
After(frameId, limit)
Before(frameId, limit)
```

Meanings:

- `Latest`: initial tail load or jump to end.
- `After`: live tail continuation.
- `Before`: scrolling backward in live or archive history.

### Query Result

```text
FrameQueryResult
  Source
  Status
  Frames
  FirstAvailableFrameId
  LastAvailableFrameId
  RequestedAnchorFrameId
  MessageKey
  MessageArgs
```

### Availability Status

```text
Ok
NoFrames
NoMoreBefore
NoMoreAfter
DataEvicted
SourceUnavailable
ArchiveDisabled
ArchiveError
InvalidQuery
```

Definitions:

- `Ok`: source was read successfully.
- `NoFrames`: source is available but contains no frames.
- `NoMoreBefore`: query reached the first available frame.
- `NoMoreAfter`: query reached the last available frame.
- `DataEvicted`: requested live-spool data was cleaned.
- `SourceUnavailable`: source cannot be opened or read.
- `ArchiveDisabled`: archive was requested but is not enabled for the session.
- `ArchiveError`: archive exists or is enabled but cannot currently be read.
- `InvalidQuery`: invalid source, limit, session, or anchor parameters.

Viewer behavior:

- Do not silently convert `ArchiveError` or `ArchiveDisabled` into live spool
  reads.
- Display `DataEvicted` as a visible history gap.
- Treat `NoMoreBefore` and `NoMoreAfter` as navigation boundaries, not errors.

This status model is intentionally more precise than a nullable frame list.
It prevents empty results from hiding archive errors, disabled archive state, or
spool cleanup loss.

## Search

Search remains part of the broader v0.6.0 roadmap, but it is not part of this
storage MVP's deep implementation.

MVP search, when implemented, is a message-viewer navigation aid:

- query the selected source;
- return matching frames or frame IDs to the message viewer;
- do not build a persistent search index;
- do not persist search results;
- do not implement decoded-field search;
- do not introduce a search DSL.

## Export

Export writes complete available data from one selected source:

```text
LiveSpool
Archive
```

The export command captures the source range at command start.

- `LiveSpool`: `FirstAvailableFrameId` through current `LastAvailableFrameId`.
- `Archive`: all frames currently available in archive.

Frames appended after export starts are not part of the export. If cleanup or
source loss affects the captured range during export, the export reports partial
failure or source data loss.

### File Format

File extension:

```text
.cclog
```

Header:

```text
CCLOG/1
format: plain|slim|detailed-jsonl
source: LiveSpool|Archive
exportedAtUtc: 2026-05-04T12:00:00.000Z
app: ComCross
contentVersion: 1

```

Body:

- `plain`: one rendered raw-data line per frame.
- `slim`: one `RX|TX` plus rendered raw-data line per frame.
- `detailed-jsonl`: one JSON object per frame.

Detailed JSONL frame:

```json
{
  "version": 1,
  "frameId": 1,
  "timestampUtc": "2026-05-04T12:00:00.000Z",
  "direction": "RX",
  "source": "udp",
  "attributes": {
    "remoteEndpoint": "192.168.1.2:9000"
  },
  "payloadHex": "010300000002C40B"
}
```

Export is write-only in v0.6.0. `.cclog` is not an archive package and is not
imported back into ComCross.

## Rendering

Rendering is shared by viewer and export where practical.

Rules:

- Text format defaults to UTF-8.
- Binary-safe output is required.
- Control characters must be visually distinct from normal characters.
- Suggested control display: `<NUL>`, `<CR>`, `<LF>`, `<TAB>`, and `<0x1B>`.
- Long-payload truncation is a viewer concern only. Export writes full payloads
  from the captured source range.

## Calibration And Degradation

### Goal

Calibration protects the landed-first architecture on older machines and slow
HDDs while releasing safe fixed-capacity tiers on faster hardware. It is not a
benchmark platform.

### Storage Tier

```text
Conservative
Limited
Normal
HighCapacity
```

Meanings:

- `Conservative`: unknown environment, failed calibration, or changed
  fingerprint.
- `Limited`: slow but usable storage path.
- `Normal`: storage path supports default targets.
- `HighCapacity`: storage path has clear safe headroom.

The tier maps to fixed parameters such as queue limits, segment size, flush
interval, warning thresholds, and archive batch size. MVP does not continuously
retune these parameters.

The MVP may start with conservative constants for all tiers and then widen only
the safest parameters after calibration. The contract is the tier and health
surface, not the exact numeric thresholds.

### Storage Health

```text
Healthy
Busy
Degraded
LosingData
ArchiveError
Unavailable
```

Meanings:

- `Healthy`: within thresholds.
- `Busy`: short-term pressure but no data impact.
- `Degraded`: sustained pressure; background work should be reduced.
- `LosingData`: frame loss, visible cleanup loss, or queue overflow occurred.
- `ArchiveError`: archive failed independently of spool.
- `Unavailable`: spool cannot write or read.

Tier describes calibrated capacity. Health describes current runtime behavior.

### Calibration Algorithm

MVP calibration:

1. Write a temporary test file under the active storage root.
2. Append representative small and medium frame records.
3. Flush a small number of times.
4. Read one or more frame windows.
5. Delete test files.
6. Classify the result into a tier.

Measured values may include:

- append frames per second;
- append bytes per second;
- flush p95 latency;
- read-window p95 latency.

Thresholds are implementation constants in the MVP. They can be adjusted after
field validation.

### Fingerprint

The default fingerprint uses privacy-minimal inputs:

- OS bucket;
- architecture;
- CPU logical-count bucket;
- memory bucket;
- data root hash;
- filesystem type when cheap and available;
- calibration schema version.

If future calibration needs sensitive hardware identifiers, the app must ask the
user first, explain the reason, and persist only a hash. If the user declines,
ComCross continues with the low-precision fingerprint and conservative behavior
when needed.

The MVP should not read sensitive hardware identifiers unless the lightweight
fingerprint proves insufficient in real use.

### Runtime Scheduling

Priority order:

```text
spool append
ingest continuity
viewer update
archive write
export
analysis
```

MVP scheduling:

- spool writes are highest priority;
- archive writes are asynchronous and bounded;
- export is background and lower priority than ingest;
- analysis consumers are lower priority than export;
- UI refresh can be coalesced under pressure.

### Degradation Actions

`Busy`:

- coalesce viewer notifications;
- avoid user-facing warning unless pressure persists.

`Degraded`:

- reduce background consumers;
- slow or pause archive/export/analysis work when needed;
- emit a storage warning;
- report storage pressure to bus pressure handling.

`LosingData`:

- latch the event until surfaced;
- notify the user;
- expose gaps in viewer/query/export.

`ArchiveError`:

- set archive state to `Error`;
- throttle notifications;
- keep spool running.

`Unavailable`:

- notify the user;
- attempt configured fallback if available;
- do not present Session Logs as reliable.

## Legacy SQLite Messages

The old `WorkspaceDatabaseService.messages` storage path is deprecated for
v0.6.0 Session Logs.

Rules:

- new v0.6 message flow does not write to the old `messages` table;
- old table data is not migrated to spool or archive;
- old table data is not treated as Session Archive;
- if old data is detected, Core surfaces a message that explains the storage
  model changed and the old SQLite data is not automatically migrated;
- users may inspect or export old SQLite data manually with external tools.

## Implementation Phases

Recommended implementation order:

1. Frame contract and frame ID allocator.
2. File spool append, manifest, latest/before/after reads.
3. Unified query service for live spool.
4. Viewer source/mode groundwork for live spool.
5. `.cclog` export from live spool.
6. Segment cleanup and evicted-state reporting.
7. Storage calibration tier and health state.
8. Archive state, async writer, and history query.
9. Archive export and archive delete handling.
10. Legacy SQLite message deprecation notice.

Each phase should keep spool append usable before adding optional archive,
export, analysis, or search depth.
