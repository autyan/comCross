param()

$ErrorActionPreference = 'Stop'

if ($env:COMCROSS_SKIP_GUARDRAILS -eq '1') {
    Write-Host "[i18n] Skipped (COMCROSS_SKIP_GUARDRAILS=1)."
    exit 0
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$py = Get-Command python -ErrorAction SilentlyContinue
$py3 = Get-Command python3 -ErrorAction SilentlyContinue

if ($py -or $py3) {
    $exe = if ($py3) { $py3.Source } else { $py.Source }
    & $exe (Join-Path $root 'repo-tools/check-shell-i18n.py') $root
    exit $LASTEXITCODE
}

# Fallback (no python): conservative regex scan.
# This is intentionally simpler and may miss cases; prefer installing python.

$shellDir = Join-Path $root 'src/Shell'
if (!(Test-Path $shellDir)) {
    Write-Error "[i18n] Shell dir not found: $shellDir"
}

$fail = $false
$files = Get-ChildItem -Path $shellDir -Recurse -Filter '*.cs' -File |
    Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]' -and $_.FullName -notmatch '[\\/]obj[\\/]' }

foreach ($f in $files) {
    $lines = Get-Content -Path $f.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        $prev = if ($i -gt 0) { $lines[$i-1] } else { '' }

        if ($line -match 'i18n-ignore' -or $prev -match 'i18n-ignore') { continue }
        if ($line -match '(?i)logger\.|\.log\(|\.log(debug|information|warning|error|critical)\(|\blog\.|serilog\.log\.|\.debug\(|\.information\(|\.warning\(|\.error\(|\.fatal\(|console\.writeline\(|debug\.writeline\(|trace\.writeline\(') { continue }
        if ($line -match '(?i)throw\s+new\s+') { continue }
        if ($line -match 'GetString\(') { continue }

        # crude: find quoted segments, then flag those with spaces + words
        $m = [regex]::Matches($line, '"([^"\\]|\\.)*"')
        foreach ($mm in $m) {
            $s = $mm.Value.Trim('"')
            if ($s -match '\s' -and $s -match '(?i)[a-z]{3,}') {
                $fail = $true
                Write-Host "[i18n] FAIL: $($f.FullName):$($i+1) raw string: $($mm.Value)" -ForegroundColor Red
            }
        }
    }
}

if ($fail) {
    throw "[i18n] Raw UI strings detected in src/Shell/**/*.cs"
}

Write-Host "[i18n] OK: No obvious raw UI strings in src/Shell/**/*.cs"
