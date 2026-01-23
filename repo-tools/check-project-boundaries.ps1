param()

$ErrorActionPreference = 'Stop'

if ($env:COMCROSS_SKIP_GUARDRAILS -eq '1') {
    Write-Host "[guardrails] Skipped (COMCROSS_SKIP_GUARDRAILS=1)."
    exit 0
}

$root = Resolve-Path (Join-Path $PSScriptRoot '..')

function Test-NoProjectReference {
    param(
        [Parameter(Mandatory=$true)][string]$Label,
        [Parameter(Mandatory=$true)][string]$RelativeDir
    )

    $dir = Join-Path $root $RelativeDir
    if (!(Test-Path $dir)) {
        Write-Error "[guardrails] $Label: directory not found: $RelativeDir"
    }

    $csprojs = Get-ChildItem -Path $dir -Recurse -Filter '*.csproj' -File -ErrorAction Stop |
        Where-Object { $_.FullName -notmatch '[\\/]bin[\\/]' -and $_.FullName -notmatch '[\\/]obj[\\/]' }

    $hits = @()
    foreach ($p in $csprojs) {
        $m = Select-String -Path $p.FullName -SimpleMatch '<ProjectReference' -ErrorAction SilentlyContinue
        if ($m) {
            $hits += $m
        }
    }

    if ($hits.Count -gt 0) {
        Write-Host "[guardrails] FAIL: $Label must not reference in-repo projects (no <ProjectReference>)." -ForegroundColor Red
        Write-Host "[guardrails] Found ProjectReference entries under $RelativeDir:" -ForegroundColor Red
        $hits | ForEach-Object { Write-Host $_.Filename ':' $_.LineNumber ':' $_.Line }
        throw "Guardrails failed for $Label"
    }

    Write-Host "[guardrails] OK: $Label has no ProjectReference."
}

Test-NoProjectReference -Label 'Platform' -RelativeDir 'src/Platform'
Test-NoProjectReference -Label 'PluginSdk' -RelativeDir 'src/PluginSdk'

Write-Host "[guardrails] All checks passed."
