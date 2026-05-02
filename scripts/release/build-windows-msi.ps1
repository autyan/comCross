param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "win-arm64")][string[]]$Rids = @("win-x64", "win-arm64"),
    [string]$OutputDir = "artifacts/release",
    [string]$CertificatePfxPath = "",
    [string]$CertificatePassword = $env:COMCROSS_WINDOWS_CERT_PASSWORD,
    [switch]$RequireSigning
)

$ErrorActionPreference = "Stop"

function Normalize-Version {
    param([Parameter(Mandatory=$true)][string]$InputVersion)
    $normalized = $InputVersion.TrimStart("v")
    if ($normalized -notmatch '^[0-9]+(\.[0-9]+){1,3}([._+-][A-Za-z0-9]+)?$') {
        throw "Invalid release version: $InputVersion"
    }
    return $normalized
}

function Assert-Command {
    param([Parameter(Mandatory=$true)][string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Get-PluginIdFromManifest {
    param([Parameter(Mandatory=$true)][string]$ManifestPath)
    $json = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Json
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

function Publish-ComCrossWindows {
    param(
        [Parameter(Mandatory=$true)][string]$Rid,
        [Parameter(Mandatory=$true)][string]$PublishDir
    )

    if (Test-Path $PublishDir) {
        Remove-Item -Recurse -Force $PublishDir
    }
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    dotnet publish src/Shell/ComCross.Shell.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false
    dotnet publish src/PluginHost/ComCross.PluginHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false
    dotnet publish src/ExtensionHost/ComCross.ExtensionHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false
    dotnet publish src/SessionHost/ComCross.SessionHost.csproj -c $Configuration -r $Rid --self-contained true -o $PublishDir -p:DebugType=none -p:DebugSymbols=false

    $pluginsDir = Join-Path $PublishDir "plugins"
    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

    Get-ChildItem -Path "src/Plugins" -Filter "*.csproj" -Recurse | ForEach-Object {
        $pluginProj = $_.FullName
        $pluginDir = Split-Path -Parent $pluginProj
        $manifestPath = Join-Path $pluginDir "Resources/ComCross.Plugin.Manifest.json"
        $pluginId = Get-PluginIdFromManifest -ManifestPath $manifestPath
        $stableHash = Get-StableHash -PluginId $pluginId
        $pluginOut = Join-Path $pluginsDir "$pluginId-$stableHash"

        dotnet publish $pluginProj -c $Configuration -r $Rid --self-contained false -o $pluginOut -p:DebugType=none -p:DebugSymbols=false
    }
}

function New-WixId {
    param([Parameter(Mandatory=$true)][string]$Value)
    $clean = [Regex]::Replace($Value, '[^A-Za-z0-9_]', '_')
    if ($clean -match '^[0-9]') {
        $clean = "I_$clean"
    }
    if ($clean.Length -gt 60) {
        $hashBytes = [System.Security.Cryptography.SHA256]::HashData([System.Text.Encoding]::UTF8.GetBytes($Value))
        $hash = [Convert]::ToHexString($hashBytes).Substring(0, 12).ToLowerInvariant()
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
        $relative = [System.IO.Path]::GetRelativePath($PublishDir, $file.FullName).Replace('\', '/')
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
Assert-Command dotnet
Assert-Command wix

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
    wix build packaging/windows/ComCross.Product.wxs $harvested `
        -d "ProductVersion=$Version" `
        -d "PlatformRid=$rid" `
        -o $msiPath

    if (-not [string]::IsNullOrWhiteSpace($CertificatePfxPath)) {
        Assert-Command signtool
        signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $CertificatePfxPath /p $CertificatePassword $msiPath
    } elseif ($RequireSigning) {
        throw "MSI signing is required but CertificatePfxPath was not provided."
    }
}

Write-Host "Windows MSI packages ready under $packageDir"
