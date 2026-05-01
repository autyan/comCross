# Plugin, Host, And IPC Rules

## Plugin Boundaries

- `PluginSdk` is the public plugin-facing surface. Keep it stable and free of in-repository project references.
- Built-in plugins should use the same SDK-facing contracts expected of external plugins.
- Do not make Shell depend on built-in plugin implementation details.

## Host Boundaries

- `PluginHost` owns plugin process hosting behavior.
- `SessionHost` owns session-scoped host behavior.
- `ExtensionHost` owns extension-host process behavior.
- Core coordinates host lifecycles and IPC. Shell should consume Shell-facing services rather than host protocol details.

## IPC And Shared Memory

- Treat message names, payloads, event types, shared-memory descriptors, and backpressure semantics as contracts.
- Any change to IPC or shared-memory behavior must be explicitly called out as a contract change.
- Use structured payload types when possible. Avoid duplicating anonymous payload shapes across UI call sites.
- Keep shared-memory allocation, cleanup, and backpressure behavior observable through logs, tests, or documentation.

## Capability And Permission Semantics

- Capability IDs, permission IDs, plugin IDs, and privilege names are external identifiers.
- Do not rename or repurpose them silently.
- Validate plugin-provided schemas and UI state at the boundary where they enter host or Shell workflows.

## Publishing And Runtime Layout

- Do not casually change application entry points, publish directory structure, plugin directory rules, host binary copying, or data directory rules.
- If a task explicitly touches release, packaging, or install behavior, verify repository scripts and document the changed contract.
