param()

$ErrorActionPreference = 'Stop'

if ($env:COMCROSS_SKIP_GUARDRAILS -eq '1') {
    Write-Host "[i18n] Skipped (COMCROSS_SKIP_GUARDRAILS=1)."
    exit 0
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$py = Get-Command python -ErrorAction SilentlyContinue
$py3 = Get-Command python3 -ErrorAction SilentlyContinue

if (!($py -or $py3)) {
    throw "[i18n] python/python3 not found. Install Python to run check-shell-i18n-keys.";
}

$exe = if ($py3) { $py3.Source } else { $py.Source }
& $exe (Join-Path $root 'repo-tools/check-shell-i18n-keys.py') $root
exit $LASTEXITCODE
