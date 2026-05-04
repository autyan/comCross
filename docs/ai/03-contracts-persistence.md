# Contract And Persistence Rules

## Contract Changes

The following are contract changes and must be called out explicitly:

- Plugin manifest structure or semantics.
- `PluginSdk` public APIs, types, enums, serialized structures, or resource strings.
- Core/Host/Plugin IPC messages, payloads, events, or shared-memory protocols.
- Workspace, config, database, or other persisted file structures and field semantics.
- Capability IDs, permission names, plugin IDs, and other external identifiers.

ComCross is pre-stable and has not entered a public compatibility period. Breaking changes are allowed when they close architecture gaps, correct runtime boundaries, or establish durable directory and packaging contracts. They must not be silent.

## Required Documentation

- At minimum, synchronize confirmed contract decisions into `dev-develop/` when that internal documentation workflow is being used.
- If the contract is public or long-lived, synchronize the relevant `/docs` content.
- `/docs` content must be English.

## Persistence Changes

Any persisted structure change must state one migration strategy:

- Discard old data and rebuild.
- Breaking relocation with no compatibility read.
- One-time conversion.
- Compatible read followed by gradual retirement.

At least one diagnostic surface must make the migration observable:

- Documentation.
- Logging.
- Test coverage.

## Session Descriptor Display State

- Message viewer display preferences that are part of a session context must be persisted on `SessionDescriptor`, not only in global settings. This includes payload render mode and message display mode.
- The compatible-read migration strategy for missing display fields is: default payload render mode to `String` and default message display mode to `Detailed`.
- Changing display mode must not change message viewer data loading semantics. The persisted display fields control Shell rendering only; `LiveSpool` / `Archive` window loading and search paths remain shared.

## Compatibility

- During the current pre-stable stage, compatibility reads and migrations are not required by default for directory, configuration, database, plugin layout, or install layout changes.
- Breaking relocation is acceptable when it is documented as the chosen strategy.
- Do not change persisted semantics while only changing UI presentation.
- Do not change defaults or field meanings without naming the compatibility impact.
- When the project enters a stable compatibility period, update this rule file before requiring compatibility reads, migrations, or long-term retention guarantees.
