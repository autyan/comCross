param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts",
    [string[]]$Rids = @("linux-x64", "linux-arm64", "win-x64", "win-arm64"),
    [switch]$Publish
)

function Get-PluginIdFromManifest {
    param(
        [Parameter(Mandatory=$true)][string]$ManifestPath
    )

    if (-not (Test-Path $ManifestPath)) {
        throw "Missing manifest: $ManifestPath"
    }

    $json = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
    $pluginId = [string]$json.id
    if ([string]::IsNullOrWhiteSpace($pluginId)) {
        throw "Manifest missing id: $ManifestPath"
    }
    return $pluginId
}

function Get-Base32 {
    param(
        [Parameter(Mandatory=$true)][byte[]]$Bytes
    )

    # RFC 4648 base32 alphabet
    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $output = New-Object System.Text.StringBuilder

    $buffer = 0
    $bitsLeft = 0

    foreach ($b in $Bytes) {
        $buffer = (($buffer -shl 8) -bor $b)
        $bitsLeft += 8

        while ($bitsLeft -ge 5) {
            $index = ($buffer -shr ($bitsLeft - 5)) -band 31
            [void]$output.Append($alphabet[$index])
            $bitsLeft -= 5
        }
    }

    if ($bitsLeft -gt 0) {
        $index = ($buffer -shl (5 - $bitsLeft)) -band 31
        [void]$output.Append($alphabet[$index])
    }

    return $output.ToString()
}

function Get-StableHash {
    param(
        [Parameter(Mandatory=$true)][string]$PluginId
    )

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($PluginId)
        $hash = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }

    $b32 = Get-Base32 -Bytes $hash
    return $b32.Substring(0, 8).ToLowerInvariant()
}

function Publish-Plugins {
    param(
        [Parameter(Mandatory=$true)][string]$OutPath,
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$Configuration
    )

    $pluginsDir = Join-Path $OutPath 'plugins'
    if (Test-Path $pluginsDir) {
        Remove-Item -Recurse -Force $pluginsDir
    }
    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

    Get-ChildItem -Path 'src/Plugins' -Filter '*.csproj' -Recurse | ForEach-Object {
        $pluginProj = $_.FullName
        $pluginDir = Split-Path -Parent $pluginProj
        $manifestPath = Join-Path $pluginDir 'Resources\ComCross.Plugin.Manifest.json'
        $pluginId = Get-PluginIdFromManifest -ManifestPath $manifestPath
        $stableHash = Get-StableHash -PluginId $pluginId
        $pluginOut = Join-Path $pluginsDir "$pluginId-$stableHash"

        dotnet publish $pluginProj -c $Configuration -r $Rid --self-contained false -o $pluginOut | Out-Host
    }
}

if ([string]::IsNullOrWhiteSpace($Configuration)) {
    Write-Error "Configuration cannot be empty."
    exit 1
}

# Repository guardrails (architectural boundaries)
if ($env:COMCROSS_SKIP_GUARDRAILS -ne '1') {
    & (Join-Path $PSScriptRoot '..\repo-tools\check-project-boundaries.ps1')

    # i18n guardrails: strict locally; warn-only in CI/CD (or when explicitly requested)
    # - COMCROSS_STRICT_I18N=1 forces strict mode even in CI
    # - COMCROSS_I18N_WARN_ONLY=1 forces warn-only mode even locally
    $warnOnly = $false
    if ($env:COMCROSS_I18N_WARN_ONLY -eq '1') {
        $warnOnly = $true
    } elseif (-not [string]::IsNullOrWhiteSpace($env:CI) -and $env:COMCROSS_STRICT_I18N -ne '1') {
        $warnOnly = $true
    }

    if ($warnOnly) {
        try {
            & (Join-Path $PSScriptRoot '..\repo-tools\check-shell-i18n.ps1')
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Shell i18n scan reported issues (exit $LASTEXITCODE); not failing CI."
            }
        } catch {
            Write-Warning "Shell i18n scan threw an error; not failing CI. $($_.Exception.Message)"
        }

        try {
            & (Join-Path $PSScriptRoot '..\repo-tools\check-shell-i18n-keys.ps1')
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "Shell i18n key check reported issues (exit $LASTEXITCODE); not failing CI."
            }
        } catch {
            Write-Warning "Shell i18n key check threw an error; not failing CI. $($_.Exception.Message)"
        }
    } else {
        & (Join-Path $PSScriptRoot '..\repo-tools\check-shell-i18n.ps1')
        & (Join-Path $PSScriptRoot '..\repo-tools\check-shell-i18n-keys.ps1')
    }
}

if ($Publish) {
    if ($Rids.Count -eq 0) {
        Write-Error "RID list cannot be empty for publish."
        exit 1
    }

    foreach ($rid in $Rids) {
        $outPath = Join-Path $OutputDir "ComCross-$rid-$Configuration"
        dotnet publish src/Shell/ComCross.Shell.csproj -c $Configuration -r $rid --self-contained false -o $outPath
        dotnet publish src/PluginHost/ComCross.PluginHost.csproj -c $Configuration -r $rid --self-contained false -o $outPath
        dotnet publish src/SessionHost/ComCross.SessionHost.csproj -c $Configuration -r $rid --self-contained false -o $outPath
        Publish-Plugins -OutPath $outPath -Rid $rid -Configuration $Configuration
    }
} else {
    dotnet build src/Shell/ComCross.Shell.csproj -c $Configuration
}
