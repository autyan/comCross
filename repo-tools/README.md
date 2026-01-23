# Repository Tools

This folder contains repository-level tooling that enforces architectural guardrails and helps keep the codebase consistent.

## Guardrails

- `check-project-boundaries.sh`
- `check-project-boundaries.ps1`
- `check-shell-i18n.sh`
- `check-shell-i18n.ps1`
- `check-shell-i18n-keys.sh`
- `check-shell-i18n-keys.ps1`

These scripts currently enforce:

- `src/Platform` must not reference any in-repo projects (no `ProjectReference`).
- `src/PluginSdk` must not reference any in-repo projects (no `ProjectReference`).
- `src/Shell/**/*.cs` must not contain obvious raw UI strings (logs are allowed). Use `// i18n-ignore` to suppress a known-safe literal.
- Shell i18n key integrity: keys referenced by Shell (`GetString("...")`, `L[foo.bar]`, etc.) must exist in the hardcoded en-US dictionary in `src/Core/Services/LocalizationService.cs`.

Notes:

- `check-shell-i18n.sh` uses `python3` for the most accurate scan.
- `check-shell-i18n.ps1` will use `python`/`python3` if available, otherwise falls back to a simpler regex-based scan.
- `check-shell-i18n-keys.(sh|ps1)` requires `python`/`python3`.

They are executed by default from:

- `scripts/build.sh`
- `scripts/build.ps1`

If you need to bypass guardrails temporarily (not recommended), set:

- Bash: `COMCROSS_SKIP_GUARDRAILS=1`
- PowerShell: `$env:COMCROSS_SKIP_GUARDRAILS = 1`
