# Guardrails And Verification Rules

## Guardrail Scripts

Keep these scripts working for their relevant areas:

- `repo-tools/check-project-boundaries.sh`
- `repo-tools/check-project-boundaries.ps1`
- `repo-tools/check-shell-i18n.sh`
- `repo-tools/check-shell-i18n.ps1`
- `repo-tools/check-shell-i18n-keys.sh`
- `repo-tools/check-shell-i18n-keys.ps1`

## Default Commands

Use these defaults unless a narrower verification is clearly sufficient:

```bash
dotnet build ComCross.sln --no-restore
bash repo-tools/check-project-boundaries.sh
```

For Shell i18n work, also run:

```bash
bash repo-tools/check-shell-i18n.sh
bash repo-tools/check-shell-i18n-keys.sh
```

## Current Guardrail Meaning

- Project boundary guardrail enforces no in-repository `ProjectReference` under `src/Platform` and `src/PluginSdk`.
- Shell i18n raw-string guardrail detects likely user-visible strings in `src/Shell/**/*.cs`.
- Shell i18n key guardrail checks Shell key references against the hardcoded `en-US` dictionary in `src/Core/Services/LocalizationService.cs`.

## Reporting

- Report commands run and results.
- Separate known unrelated warnings or failures from current-scope failures.
- Do not hide guardrail failures. If a task intentionally defers one, state that explicitly.
