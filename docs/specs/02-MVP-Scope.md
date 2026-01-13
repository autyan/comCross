# MVP Scope (Serial First)

## Goals
- Provide a stable, high-performance serial workspace.
- Build a modular tool system around the workspace.
- Persist workspace state across sessions.

## In Scope
- Serial port enumeration, connect/disconnect.
- Per-session port settings: baud, data bits, parity, stop bits, flow control, encoding.
- Multi-session tabs.
- Message stream view with search, filter, highlight.
- Send panel: STR/HEX, history, append newline.
- Metrics: RX/TX, rate, elapsed time.
- Auto-save workspace state (WorkState and Toolset).
- Plugin loader with offline expansion.

## Out of Scope (MVP)
- macOS support.
- Non-serial buses (CAN, I2C, SPI, etc.).
- Advanced scripting/automation.
- Cloud sync, team collaboration.

## Milestones
1) Linux MVP: core workspace + basic tools + state persistence.
2) Windows parity: device enumeration enhancements + installer tooling.
