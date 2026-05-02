# v0.4 Release Scope

## Goals

- Provide a stable daily-use communication workspace for serial, TCP, and UDP workflows.
- Keep bus-domain knowledge inside bus plugins and expose it through public contracts.
- Persist workspace/session descriptors clearly enough for reliable startup restoration.
- Make message display, search, quick commands, and send feedback usable for release validation.

## In Scope

- Built-in bus adapter plugins:
  - Serial adapter with plugin-owned serial port scanning.
  - TCP client.
  - TCP listener with accepted clients represented as scoped child sessions.
  - UDP client.
  - UDP listener with plugin-produced transmit targets for replies.
- Session and workload management.
- Session detail and connection parameter reuse through plugin-produced metadata.
- Message stream display with RX/TX direction, search, and bounded frame attributes.
- Send panel with STR mode, CR/LF options, clear-after-send, quick commands, and send-result errors.
- Editable quick commands with localized defaults on first initialization.
- Plugin settings pages and plugin UI-state rendering.
- Workspace persistence through v0.4 `SessionDescriptors`.
- English and Simplified Chinese UI resources.
- Release-oriented guardrails for project boundaries and Shell i18n.

## Out Of Scope For v0.4

- Compatibility migration for v0.3 session state.
- File-stream-backed message display and storage.
- Complete removal of the remaining controlled Shell static bridges.
- macOS release validation.
- Installer-level permission automation and plugin signature enforcement.
- Advanced scripting, cloud sync, and team collaboration.

## Acceptance Definition

v0.4 is accepted when:

- Serial, TCP, and UDP sessions can be created, disconnected, restored as descriptors, and deleted.
- Plugin-owned serial scanning works from the UI without Shell/Core scanning serial devices directly.
- UDP listener receives datagrams in the listener session and can reply to selected plugin-provided targets.
- Message frame attributes are stored, rendered, and searchable within the documented limits.
- Deleting a session removes its descriptor and plugin-owned session storage.
- Build, tests, project-boundary guardrail, and Shell i18n guardrails pass.
