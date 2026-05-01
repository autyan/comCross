# Plugin, Host, And IPC Rules

## Plugin Boundaries

- `PluginSdk` is the public plugin-facing surface. Keep it stable and free of in-repository project references.
- Built-in plugins should use the same SDK-facing contracts expected of external plugins.
- Do not make Shell depend on built-in plugin implementation details.
- Bus plugins produce domain facts for the main program. Core and Shell consume those facts through SDK/IPC contracts and must not infer plugin-private semantics.

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
- Model main-program/plugin interaction as command/query in, result/state/event/patch out:
  - Core/Shell may ask a plugin to connect, disconnect, execute an action, provide UI state, initialize session state, or describe resources.
  - The plugin's returned data is the authoritative source for bus-domain decisions that Core/Shell need to consume.
  - Core/Shell own validation of contract shape, lifecycle safety, persistence of public metadata, and degraded behavior when the producer is unavailable.
- Plugin settings are public producer inputs. When a plugin setting affects plugin-owned domain behavior, Core may pass a settings snapshot through the relevant IPC query, and the plugin must apply it itself.
- Do not reimplement plugin-owned discovery in Shell/Core. For example, serial port scanning belongs to the serial bus plugin; Shell only renders the plugin-provided state and dispatches plugin actions.
- If the host needs a generic pre-connect policy such as exclusive local-resource conflict prompting, the capability must declare the public resource key through `PluginConnectionResourceDescriptor`. Shell/Core may compare that declared key against committed session parameters; they must not hardcode plugin ids or plugin-private parameter names.

## Capability And Permission Semantics

- Capability IDs, permission IDs, plugin IDs, and privilege names are external identifiers.
- Do not rename or repurpose them silently.
- Validate plugin-provided schemas and UI state at the boundary where they enter host or Shell workflows.

## Publishing And Runtime Layout

- Do not casually change application entry points, publish directory structure, plugin directory rules, host binary copying, or data directory rules.
- If a task explicitly touches release, packaging, or install behavior, verify repository scripts and document the changed contract.
