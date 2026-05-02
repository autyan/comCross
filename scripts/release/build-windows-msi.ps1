param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")][string[]]$Rids = @("win-x64", "win-arm64"),
    [ValidateSet("Stable", "Dev", "EAP")][string]$Channel = "Stable",
    [string]$DirectoryName = "",
    [string]$InstanceId = "",
    [string]$SchemaLine = "v0",
    [string]$OutputDir = "artifacts/release",
    [string]$PluginSigningKeyPath = "",
    [string]$PluginSigningKeyId = "comcross-plugin-official-2026",
    [string]$CertificatePfxPath = "",
    [string]$CertificatePassword = $env:COMCROSS_WINDOWS_CERT_PASSWORD,
    [switch]$RequirePluginSigning,
    [switch]$RequireSigning
)

$ErrorActionPreference = "Stop"
$env:MSBUILDDISABLENODEREUSE = "1"
$DotNetPublishBuildArgs = @("-maxcpucount:1", "-nodeReuse:false")

function Normalize-Version {
    param([Parameter(Mandatory=$true)][string]$InputVersion)
    $normalized = $InputVersion.TrimStart("v")
    if ($normalized -notmatch '^[0-9]+(\.[0-9]+){1,3}(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
        throw "Invalid release version: $InputVersion"
    }
    return $normalized
}

function Get-MsiProductVersion {
    param([Parameter(Mandatory=$true)][string]$InputVersion)

    if ($InputVersion -notmatch '^([0-9]+)(?:\.([0-9]+))?(?:\.([0-9]+))?') {
        throw "MSI product version requires numeric major/minor/patch components: $InputVersion"
    }

    $major = [int]$Matches[1]
    $minor = if ($Matches[2]) { [int]$Matches[2] } else { 0 }
    $patch = if ($Matches[3]) { [int]$Matches[3] } else { 0 }
    if ($major -ge 256 -or $minor -ge 256 -or $patch -ge 65536) {
        throw "MSI product version is out of range: $major.$minor.$patch"
    }

    return "$major.$minor.$patch"
}

function Assert-Command {
    param([Parameter(Mandatory=$true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Invoke-Native {
    if ($args.Count -lt 1) {
        throw "Invoke-Native requires a command."
    }

    $Command = [string]$args[0]
    $Arguments = @($args | Select-Object -Skip 1)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command $($Arguments -join ' ')"
    }
}

function Get-DefaultDirectoryName {
    param([Parameter(Mandatory=$true)][string]$InputChannel)
    switch ($InputChannel) {
        "Stable" { return "ComCross" }
        "Dev" { return "ComCrossDev" }
        "EAP" { return "ComCrossEAP" }
        default { throw "Unsupported channel: $InputChannel" }
    }
}

function Get-DefaultInstanceId {
    param([Parameter(Mandatory=$true)][string]$InputChannel)
    switch ($InputChannel) {
        "Stable" { return "comcross-stable" }
        "Dev" { return "comcross-dev" }
        "EAP" { return "comcross-eap" }
        default { throw "Unsupported channel: $InputChannel" }
    }
}

function Get-DefaultProductName {
    param([Parameter(Mandatory=$true)][string]$InputChannel)
    switch ($InputChannel) {
        "Stable" { return "ComCross" }
        "Dev" { return "ComCross Dev" }
        "EAP" { return "ComCross EAP" }
        default { throw "Unsupported channel: $InputChannel" }
    }
}

function Get-UpgradeCode {
    param([Parameter(Mandatory=$true)][string]$InputChannel)
    switch ($InputChannel) {
        "Stable" { return "2D0F7581-E54A-4E77-8F17-0DD6E82290E1" }
        "Dev" { return "DEA4F4F0-42B8-45A9-8307-E887D5FEECCB" }
        "EAP" { return "817B2890-6522-4871-9F63-18BA70B07422" }
        default { throw "Unsupported channel: $InputChannel" }
    }
}

function Write-InstanceManifest {
    param([Parameter(Mandatory=$true)][string]$PublishDir)

    $manifest = [ordered]@{
        schemaVersion = 1
        product = "ComCross"
        instanceId = $InstanceId
        channel = $Channel.ToLowerInvariant()
        schemaLine = $SchemaLine
        directoryName = $DirectoryName
    }

    $manifestPath = Join-Path $PublishDir "ComCross.Instance.json"
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path $manifestPath -Encoding UTF8
}

function Sign-PluginPackage {
    param([Parameter(Mandatory=$true)][string]$PluginDir)

    if ([string]::IsNullOrWhiteSpace($PluginSigningKeyPath)) {
        return
    }

    Invoke-Native dotnet run --project src/Tools/ComCross.Tools.csproj -- `
        sign-plugin `
        --plugin-dir $PluginDir `
        --private-key $PluginSigningKeyPath `
        --key-id $PluginSigningKeyId
}

function Get-PluginIdFromManifest {
    param([Parameter(Mandatory=$true)][string]$ManifestPath)
    $json = Get-Content -Raw -Encoding UTF8 -Path $ManifestPath | ConvertFrom-Json
    $pluginId = [string]$json.id
    if ([string]::IsNullOrWhiteSpace($pluginId)) {
        throw "Manifest missing id: $ManifestPath"
    }
    return $pluginId
}

function Get-Base32 {
    param([Parameter(Mandatory=$true)][byte[]]$Bytes)
    $alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567"
    $output = [System.Text.StringBuilder]::new()
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
    param([Parameter(Mandatory=$true)][string]$PluginId)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($PluginId)
        $hash = $sha.ComputeHash($bytes)
    } finally {
        $sha.Dispose()
    }
    return (Get-Base32 -Bytes $hash).Substring(0, 8).ToLowerInvariant()
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory=$true)][string]$BasePath,
        [Parameter(Mandatory=$true)][string]$TargetPath
    )

    $baseFullPath = (Resolve-Path -LiteralPath $BasePath).ProviderPath.TrimEnd('\', '/')
    $targetFullPath = (Resolve-Path -LiteralPath $TargetPath).ProviderPath
    $prefix = $baseFullPath + [System.IO.Path]::DirectorySeparatorChar
    if (-not $targetFullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Target path is not under base path. Base: $baseFullPath Target: $targetFullPath"
    }
    return $targetFullPath.Substring($prefix.Length)
}

function Publish-ComCrossWindows {
    param(
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$PublishDir
    )

    if (Test-Path $PublishDir) {
        Remove-Item -Recurse -Force $PublishDir
    }
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    Invoke-Native dotnet publish src/Shell/ComCross.Shell.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs
    Invoke-Native dotnet publish src/Startup/ComCross.Startup.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs
    Invoke-Native dotnet publish src/PluginHost/ComCross.PluginHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs
    Invoke-Native dotnet publish src/ExtensionHost/ComCross.ExtensionHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs
    Invoke-Native dotnet publish src/SessionHost/ComCross.SessionHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs

    Write-InstanceManifest -PublishDir $PublishDir

    $legacyPluginsDir = Join-Path $PublishDir "plugins"
    if (Test-Path $legacyPluginsDir) {
        Remove-Item -Recurse -Force $legacyPluginsDir
    }

    $pluginsDir = Join-Path $PublishDir "bundled-plugins"
    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

    Get-ChildItem -Path "src/Plugins" -Filter "*.csproj" -Recurse | ForEach-Object {
        $pluginProj = $_.FullName
        $pluginDir = Split-Path -Parent $pluginProj
        $manifestPath = Join-Path $pluginDir "Resources/ComCross.Plugin.Manifest.json"
        $pluginId = Get-PluginIdFromManifest -ManifestPath $manifestPath
        $stableHash = Get-StableHash -PluginId $pluginId
        $pluginOut = Join-Path $pluginsDir "$pluginId-$stableHash"

        Invoke-Native dotnet publish $pluginProj -c $Configuration -r $Rid --self-contained false -o $pluginOut -p:DebugType=none -p:DebugSymbols=false @DotNetPublishBuildArgs
        Sign-PluginPackage -PluginDir $pluginOut
    }
}

function New-WixId {
    param([Parameter(Mandatory=$true)][string]$Value)
    $clean = [Regex]::Replace($Value, '[^A-Za-z0-9_]', '_')
    if ($clean -match '^[0-9]') {
        $clean = "I_$clean"
    }
    if ($clean.Length -gt 60) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Value))
        } finally {
            $sha.Dispose()
        }
        $hash = [BitConverter]::ToString($hashBytes).Replace("-", "").Substring(0, 12).ToLowerInvariant()
        $clean = $clean.Substring(0, 47) + "_" + $hash
    }
    return $clean
}

function New-HarvestedWxs {
    param(
        [Parameter(Mandatory=$true)][string]$PublishDir,
        [Parameter(Mandatory=$true)][string]$OutputPath
    )

    $files = Get-ChildItem -Path $PublishDir -File -Recurse | Sort-Object FullName
    $root = [ordered]@{
        Id = "INSTALLFOLDER"
        Name = ""
        Dirs = [ordered]@{}
        Files = @()
    }

    foreach ($file in $files) {
        $relative = (Get-RelativePath -BasePath $PublishDir -TargetPath $file.FullName).Replace('\', '/')
        $parts = $relative.Split('/')
        $node = $root
        if ($parts.Count -gt 1) {
            for ($i = 0; $i -lt $parts.Count - 1; $i++) {
                $dirPath = ($parts[0..$i] -join '/')
                if (-not $node.Dirs.Contains($parts[$i])) {
                    $node.Dirs[$parts[$i]] = [ordered]@{
                        Id = "Dir_" + (New-WixId -Value $dirPath)
                        Name = $parts[$i]
                        Dirs = [ordered]@{}
                        Files = @()
                    }
                }
                $node = $node.Dirs[$parts[$i]]
            }
        }
        $node.Files += [ordered]@{
            Relative = $relative
            Source = $file.FullName
        }
    }

    $componentRefs = [System.Text.StringBuilder]::new()
    $directoryXml = [System.Text.StringBuilder]::new()

    function Write-DirectoryNode {
        param(
            [Parameter(Mandatory=$true)]$Node,
            [Parameter(Mandatory=$true)][System.Text.StringBuilder]$Builder,
            [Parameter(Mandatory=$true)][System.Text.StringBuilder]$Refs,
            [int]$Indent = 2,
            [switch]$RootNode
        )

        $pad = " " * $Indent
        if ($RootNode) {
            [void]$Builder.AppendLine("$pad<DirectoryRef Id=`"INSTALLFOLDER`">")
        } else {
            $dirName = [System.Security.SecurityElement]::Escape([string]$Node.Name)
            [void]$Builder.AppendLine("$pad<Directory Id=`"$($Node.Id)`" Name=`"$dirName`">")
        }

        foreach ($file in $Node.Files) {
            $componentId = "Cmp_" + (New-WixId -Value $file.Relative)
            $fileId = "File_" + (New-WixId -Value $file.Relative)
            $source = [System.Security.SecurityElement]::Escape($file.Source)
            [void]$Builder.AppendLine("$pad  <Component Id=`"$componentId`" Guid=`"*`">")
            [void]$Builder.AppendLine("$pad    <File Id=`"$fileId`" Source=`"$source`" KeyPath=`"yes`" />")
            [void]$Builder.AppendLine("$pad  </Component>")
            [void]$Refs.AppendLine("      <ComponentRef Id=`"$componentId`" />")
        }

        foreach ($child in $Node.Dirs.Values) {
            Write-DirectoryNode -Node $child -Builder $Builder -Refs $Refs -Indent ($Indent + 2)
        }

        if ($RootNode) {
            [void]$Builder.AppendLine("$pad</DirectoryRef>")
        } else {
            [void]$Builder.AppendLine("$pad</Directory>")
        }
    }

    Write-DirectoryNode -Node $root -Builder $directoryXml -Refs $componentRefs -RootNode

    $wxs = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
$directoryXml
  </Fragment>
  <Fragment>
    <ComponentGroup Id="AppComponents">
$componentRefs
    </ComponentGroup>
  </Fragment>
</Wix>
"@
    Set-Content -Path $OutputPath -Value $wxs -Encoding UTF8
}

$Version = Normalize-Version -InputVersion $Version
$MsiProductVersion = Get-MsiProductVersion -InputVersion $Version
Assert-Command dotnet

if ([string]::IsNullOrWhiteSpace($DirectoryName)) {
    $DirectoryName = Get-DefaultDirectoryName -InputChannel $Channel
}
if ([string]::IsNullOrWhiteSpace($InstanceId)) {
    $InstanceId = Get-DefaultInstanceId -InputChannel $Channel
}
$ProductName = Get-DefaultProductName -InputChannel $Channel
$UpgradeCode = Get-UpgradeCode -InputChannel $Channel
if ($RequirePluginSigning -and [string]::IsNullOrWhiteSpace($PluginSigningKeyPath)) {
    throw "Plugin signing is required but PluginSigningKeyPath was not provided."
}
if (-not [string]::IsNullOrWhiteSpace($PluginSigningKeyPath) -and -not (Test-Path $PluginSigningKeyPath)) {
    throw "Plugin signing key was not found: $PluginSigningKeyPath"
}

Invoke-Native dotnet tool restore

$packageDir = Join-Path $OutputDir "packages/windows"
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

foreach ($rid in $Rids) {
    $publishDir = Join-Path $OutputDir "self-contained/ComCross-$rid-$Configuration"
    Publish-ComCrossWindows -Rid $rid -PublishDir $publishDir

    $workDir = Join-Path $OutputDir "wix/$rid"
    if (Test-Path $workDir) {
        Remove-Item -Recurse -Force $workDir
    }
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    $harvested = Join-Path $workDir "Harvested.wxs"
    New-HarvestedWxs -PublishDir $publishDir -OutputPath $harvested

    $msiPath = Join-Path $packageDir "ComCross-$Version-$rid.msi"
    Invoke-Native dotnet tool run wix -- build -acceptEula wix7 packaging/windows/ComCross.Product.wxs $harvested `
        -d "ProductVersion=$Version" `
        -d "PackageVersion=$MsiProductVersion" `
        -d "PlatformRid=$rid" `
        -d "ProductName=$ProductName" `
        -d "UpgradeCode=$UpgradeCode" `
        -d "InstallFolderName=$DirectoryName" `
        -d "ShortcutName=$ProductName" `
        -o $msiPath
    if (-not (Test-Path $msiPath)) {
        throw "WiX completed without producing expected MSI: $msiPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePfxPath)) {
        Assert-Command signtool
        Invoke-Native signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $CertificatePfxPath /p $CertificatePassword $msiPath
    } elseif ($RequireSigning) {
        throw "MSI signing is required but CertificatePfxPath was not provided."
    }
}

Write-Host "Windows MSI packages ready under $packageDir"
