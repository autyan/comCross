# Contract And Persistence Rules

## Contract Changes

The following are contract changes and must be called out explicitly:

- Plugin manifest structure or semantics.
- `PluginSdk` public APIs, types, enums, serialized structures, or resource strings.
- Core/Host/Plugin IPC messages, payloads, events, or shared-memory protocols.
- Workspace, config, database, or other persisted file structures and field semantics.
- Capability IDs, permission names, plugin IDs, and other external identifiers.

ComCross is pre-1.0, so breaking changes are allowed when they close architecture gaps. They must not be silent.

## Required Documentation

- At minimum, synchronize confirmed contract decisions into `dev-develop/` when that internal documentation workflow is being used.
- If the contract is public or long-lived, synchronize the relevant `/docs` content.
- `/docs` content must be English.

## Persistence Changes

Any persisted structure change must state one migration strategy:

- Discard old data and rebuild.
- One-time conversion.
- Compatible read followed by gradual retirement.

At least one diagnostic surface must make the migration observable:

- Documentation.
- Logging.
- Test coverage.

## Compatibility

- Do not change persisted semantics while only changing UI presentation.
- Do not change defaults or field meanings without naming the compatibility impact.
- When in doubt, add compatibility read paths before changing write paths.
