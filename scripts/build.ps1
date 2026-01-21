param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts",
    [string[]]$Rids = @("linux-x64", "linux-arm64", "win-x64", "win-arm64"),
    [switch]$Publish
)

function New-RandomPluginDirName {
    return ([guid]::NewGuid().ToString('N'))
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
        $pluginId = New-RandomPluginDirName
        $pluginOut = Join-Path $pluginsDir $pluginId

        dotnet publish $pluginProj -c $Configuration -r $Rid --self-contained false -o $pluginOut | Out-Host
    }
}

if ([string]::IsNullOrWhiteSpace($Configuration)) {
    Write-Error "Configuration cannot be empty."
    exit 1
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
        Publish-Plugins -OutPath $outPath -Rid $rid -Configuration $Configuration
    }
} else {
    dotnet build src/Shell/ComCross.Shell.csproj -c $Configuration
}
