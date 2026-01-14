param(
    [string]$Configuration = "Release",
    [string]$OutputDir = "artifacts",
    [string[]]$Rids = @("linux-x64", "linux-arm64", "win-x64", "win-arm64"),
    [switch]$Publish
)

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
    }
} else {
    dotnet build src/Shell/ComCross.Shell.csproj -c $Configuration
}
