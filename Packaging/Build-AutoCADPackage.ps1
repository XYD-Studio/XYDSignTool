[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$PluginVersion = "1.1.0",
    [string]$InstallerVersionLabel = "V1.1",
    [string]$CacheRoot = (Join-Path $env:LOCALAPPDATA "XYDSignTool\ObjectARX"),
    [string]$ObjectArx2018Root,
    [string]$ObjectArx2020Root,
    [string]$ObjectArx2025Root,
    [string]$ObjectArx2026Root,
    [switch]$SkipSdkDownload,
    [switch]$OpenSdkDownloadPage,
    [int]$WaitForSdkDownloadMinutes = 0,
    [switch]$SkipInstaller,
    [switch]$ContinueWithoutNet8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$LegacyProject = Join-Path $RepoRoot "XYDSignTool.csproj"
$Net8Project = Join-Path $RepoRoot "XYDSignTool.Net8.csproj"
$ArtifactsRoot = Join-Path $RepoRoot "artifacts\cad-package"
$BundleRoot = Join-Path $RepoRoot "Installer\Build\XYDSignTool.bundle"
$InstallerScript = Join-Path $RepoRoot "Packaging\XYD_Toolkit_Setup.iss"
$LatestObjectArxLicenseUrl = "https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-licensing"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Resolve-FullPath {
    param([string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Remove-DirectorySafe {
    param([string]$Path)

    $full = Resolve-FullPath $Path
    $repo = Resolve-FullPath $RepoRoot
    if (-not $full.StartsWith($repo, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a directory outside the repository: $full"
    }

    if (Test-Path -LiteralPath $full) {
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}

function Get-MSBuildPath {
    $known = @(
        "D:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($path in $known) {
        if (Test-Path -LiteralPath $path) { return $path }
    }

    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path -LiteralPath $vswhere) {
        $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($found) { return $found }
    }

    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    throw "MSBuild.exe was not found. Install Visual Studio Build Tools or pass a machine with MSBuild."
}

function Get-IsccPath {
    $known = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $known) {
        if (Test-Path -LiteralPath $path) { return $path }
    }

    $cmd = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    throw "Inno Setup compiler ISCC.exe was not found."
}

function Test-AcadRefDir {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $required = @("AcMgd.dll", "AcDbMgd.dll", "AcCoreMgd.dll", "AdWindows.dll")
    foreach ($name in $required) {
        $match = Get-ChildItem -LiteralPath $Path -File -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $match) { return $false }
    }

    return $true
}

function Test-NetFxTargetingPack {
    param([string]$Version)

    $path = Join-Path "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework" $Version
    return (Test-Path -LiteralPath (Join-Path $path "mscorlib.dll"))
}

function Get-NetFxTargetingVersion {
    param([string[]]$PreferredVersions)

    foreach ($version in $PreferredVersions) {
        if (Test-NetFxTargetingPack $version) { return $version }
    }

    throw "None of the required .NET Framework targeting packs is installed: $($PreferredVersions -join ', ')"
}

function Get-NativeSafePath {
    param([string]$Path)

    $full = Resolve-FullPath $Path
    if ($full.IndexOf(' ') -lt 0) { return $full }

    try {
        $fso = New-Object -ComObject Scripting.FileSystemObject
        if (Test-Path -LiteralPath $full -PathType Container) {
            return $fso.GetFolder($full).ShortPath
        }
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            return $fso.GetFile($full).ShortPath
        }
    }
    catch { }

    return $full
}

function Get-ObjectPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Find-AcadRefDir {
    param([string]$Root)

    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root)) {
        return $null
    }

    if (Test-AcadRefDir $Root) {
        return (Resolve-FullPath $Root)
    }

    $acmgd = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter "AcMgd.dll" -ErrorAction SilentlyContinue |
        Select-Object -First 100

    foreach ($file in $acmgd) {
        if (Test-AcadRefDir $file.DirectoryName) {
            return (Resolve-FullPath $file.DirectoryName)
        }
    }

    return $null
}

function Find-InstalledAutoCadRefDir {
    param([string[]]$Series)

    $registryRoots = @("HKLM:\SOFTWARE\Autodesk\AutoCAD", "HKCU:\SOFTWARE\Autodesk\AutoCAD")
    foreach ($root in $registryRoots) {
        foreach ($seriesName in $Series) {
            $seriesPath = Join-Path $root $seriesName
            if (-not (Test-Path -LiteralPath $seriesPath)) { continue }

            foreach ($productKey in Get-ChildItem -LiteralPath $seriesPath -ErrorAction SilentlyContinue) {
                $props = Get-ItemProperty -LiteralPath $productKey.PSPath -ErrorAction SilentlyContinue
                $location = Get-ObjectPropertyValue -Object $props -Name "AcadLocation"
                if ([string]::IsNullOrWhiteSpace($location)) { continue }

                $acadExe = Join-Path $location "acad.exe"
                if ((Test-Path -LiteralPath $acadExe) -and (Test-AcadRefDir $location)) {
                    return (Resolve-FullPath $location)
                }
            }
        }
    }

    return $null
}

function Ensure-ObjectArxSdk {
    param(
        [int]$Year,
        [string]$Url,
        [string]$FileName,
        [string]$ProvidedRoot
    )

    if ($ProvidedRoot) {
        $providedRefDir = Find-AcadRefDir $ProvidedRoot
        if ($providedRefDir) { return $providedRefDir }
        throw "The provided ObjectARX $Year root does not contain AutoCAD managed reference DLLs: $ProvidedRoot"
    }

    if (Test-Path -LiteralPath $CacheRoot) {
        foreach ($dir in Get-ChildItem -LiteralPath $CacheRoot -Directory -Filter "*$Year*" -ErrorAction SilentlyContinue) {
            $cachedRefDir = Find-AcadRefDir $dir.FullName
            if ($cachedRefDir) { return $cachedRefDir }
        }
    }

    if ($SkipSdkDownload) {
        throw "ObjectARX $Year SDK was not found in $CacheRoot and -SkipSdkDownload was specified."
    }

    New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
    $downloadPath = Join-Path $CacheRoot $FileName
    if (-not (Test-Path -LiteralPath $downloadPath)) {
        Write-Step "Downloading ObjectARX $Year SDK"
        Invoke-WebRequest -Uri $Url -OutFile $downloadPath -UseBasicParsing
    }

    $extractRoot = Join-Path $CacheRoot ([string]$Year)
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Write-Step "Extracting ObjectARX $Year SDK"
    $process = Start-Process -FilePath $downloadPath -ArgumentList @("-suppresslaunch", "-d", $extractRoot) -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "ObjectARX $Year SDK extractor failed with exit code $($process.ExitCode)."
    }

    $refDir = Find-AcadRefDir $extractRoot
    if (-not $refDir) {
        throw "ObjectARX $Year SDK was extracted but no managed reference directory was found under $extractRoot."
    }

    return $refDir
}

function Find-ObjectArxInstaller {
    param([int]$Year)

    $searchRoots = @(
        $CacheRoot,
        (Join-Path $env:USERPROFILE "Downloads"),
        (Join-Path $env:USERPROFILE "Desktop")
    )

    foreach ($root in $searchRoots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }

        $installer = Get-ChildItem -LiteralPath $root -Recurse -File -Include "*.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "ObjectARX" -and $_.Name -match [string]$Year } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($installer) {
            return $installer.FullName
        }
    }

    return $null
}

function Wait-ForObjectArxInstaller {
    param(
        [int[]]$Years,
        [int]$Minutes
    )

    if ($Minutes -le 0) { return $null }

    $deadline = (Get-Date).AddMinutes($Minutes)
    while ((Get-Date) -lt $deadline) {
        foreach ($year in $Years) {
            $installer = Find-ObjectArxInstaller -Year $year
            if ($installer) { return $installer }
        }

        Write-Host "Waiting for ObjectARX 2025/2026 installer in Downloads/cache..."
        Start-Sleep -Seconds 10
    }

    return $null
}

function Try-Extract-ObjectArxInstaller {
    param(
        [int]$Year,
        [string]$InstallerPath
    )

    if ([string]::IsNullOrWhiteSpace($InstallerPath) -or -not (Test-Path -LiteralPath $InstallerPath)) {
        return $null
    }

    New-Item -ItemType Directory -Force -Path $CacheRoot | Out-Null
    $extractRoot = Join-Path $CacheRoot ([string]$Year)
    if (Test-Path -LiteralPath $extractRoot) {
        Remove-Item -LiteralPath $extractRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

    Write-Step "Extracting ObjectARX $Year SDK from $InstallerPath"
    $attempts = @(
        @("-y", "-o$extractRoot"),
        @("-suppresslaunch", "-d", $extractRoot)
    )

    foreach ($arguments in $attempts) {
        $process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(180000)) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            continue
        }

        if ($process.ExitCode -ne 0) {
            continue
        }

        $refDir = Find-AcadRefDir $extractRoot
        if ($refDir) { return $refDir }
    }

    throw "ObjectARX $Year SDK extractor completed no usable reference directory. Installer: $InstallerPath"
}

function Resolve-Net8RefDir {
    if ($ObjectArx2026Root) {
        $refDir = Find-AcadRefDir $ObjectArx2026Root
        if ($refDir) { return $refDir }
        throw "The provided ObjectARX 2026 root does not contain AutoCAD managed reference DLLs: $ObjectArx2026Root"
    }

    if ($ObjectArx2025Root) {
        $refDir = Find-AcadRefDir $ObjectArx2025Root
        if ($refDir) { return $refDir }
        throw "The provided ObjectARX 2025 root does not contain AutoCAD managed reference DLLs: $ObjectArx2025Root"
    }

    if (Test-Path -LiteralPath $CacheRoot) {
        foreach ($dir in Get-ChildItem -LiteralPath $CacheRoot -Directory -Filter "*2026*" -ErrorAction SilentlyContinue) {
            $refDir = Find-AcadRefDir $dir.FullName
            if ($refDir) { return $refDir }
        }
        foreach ($dir in Get-ChildItem -LiteralPath $CacheRoot -Directory -Filter "*2025*" -ErrorAction SilentlyContinue) {
            $refDir = Find-AcadRefDir $dir.FullName
            if ($refDir) { return $refDir }
        }
    }

    $installed = Find-InstalledAutoCadRefDir @("R25.1", "R25.0")
    if ($installed) { return $installed }

    foreach ($year in @(2026, 2025)) {
        $installer = Find-ObjectArxInstaller -Year $year
        if ($installer) {
            $refDir = Try-Extract-ObjectArxInstaller -Year $year -InstallerPath $installer
            if ($refDir) { return $refDir }
        }
    }

    if ($OpenSdkDownloadPage) {
        Write-Warning "Opening Autodesk's ObjectARX SDK licensing page. Complete Autodesk's license/reCAPTCHA flow, then rerun this build script."
        Start-Process $LatestObjectArxLicenseUrl | Out-Null

        $installer = Wait-ForObjectArxInstaller -Years @(2026, 2025) -Minutes $WaitForSdkDownloadMinutes
        if ($installer) {
            $year = if ($installer -match "2026") { 2026 } else { 2025 }
            $refDir = Try-Extract-ObjectArxInstaller -Year $year -InstallerPath $installer
            if ($refDir) { return $refDir }
        }
    }

    return $null
}

function Invoke-Process {
    param(
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $RepoRoot
    )

    Write-Host "& `"$FilePath`" $($Arguments -join ' ')"
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($output) {
        $output | ForEach-Object { Write-Host $_ }
    }
    if ($exitCode -ne 0) {
        throw "Command failed with exit code $($exitCode): $FilePath"
    }
}

function Copy-DirectoryContents {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) { return }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $Destination -Recurse -Force
    }
}

function Copy-PluginPayload {
    param(
        [string]$BuildOutputDir,
        [string]$DestinationDir
    )

    New-Item -ItemType Directory -Force -Path $DestinationDir | Out-Null

    $dll = Join-Path $BuildOutputDir "XYDSignTool.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "Build output does not contain XYDSignTool.dll: $BuildOutputDir"
    }
    Copy-Item -LiteralPath $dll -Destination $DestinationDir -Force

    $pdb = Join-Path $BuildOutputDir "XYDSignTool.pdb"
    if (Test-Path -LiteralPath $pdb) {
        Copy-Item -LiteralPath $pdb -Destination $DestinationDir -Force
    }

    $blockSources = @(
        (Join-Path $RepoRoot "Blocks"),
        (Join-Path $RepoRoot "Installer\Contents\Blocks"),
        (Join-Path $RepoRoot "bin\x64\Debug\Blocks")
    )
    foreach ($source in $blockSources) {
        if (Test-Path -LiteralPath $source) {
            Copy-DirectoryContents $source (Join-Path $DestinationDir "Blocks")
            break
        }
    }

    Copy-DirectoryContents (Join-Path $RepoRoot "Lisp") (Join-Path $DestinationDir "Lisp")
    Copy-DirectoryContents (Join-Path $RepoRoot "Installer\Contents\Lisp") (Join-Path $DestinationDir "Lisp")

    Copy-DirectoryContents (Join-Path $RepoRoot "PMPFiles") (Join-Path $DestinationDir "PMPFiles")
    Copy-DirectoryContents (Join-Path $RepoRoot "Installer\Contents\PMPFiles") (Join-Path $DestinationDir "PMPFiles")
}

function Add-PackageComponentXml {
    param(
        [System.Text.StringBuilder]$Builder,
        [string]$Name,
        [string]$ModuleDir,
        [string]$SeriesMin,
        [string]$SeriesMax
    )

    [void]$Builder.AppendLine("  <Components>")
    [void]$Builder.AppendLine("    <RuntimeRequirements OS=""Win64"" Platform=""AutoCAD*"" SeriesMin=""$SeriesMin"" SeriesMax=""$SeriesMax"" />")
    [void]$Builder.AppendLine("    <ComponentEntry AppName=""$Name"" ModuleName=""./Contents/$ModuleDir/XYDSignTool.dll"" AppDescription=""XYDSignTool AutoCAD plugin"" LoadOnAutoCADStartup=""True"" LoadOnCommandInvocation=""False"" />")
    [void]$Builder.AppendLine("  </Components>")
}

function Write-PackageContentsXml {
    param(
        [object[]]$Segments,
        [string]$Path
    )

    $xml = [System.Text.StringBuilder]::new()
    [void]$xml.AppendLine("<?xml version=""1.0"" encoding=""utf-8""?>")
    [void]$xml.AppendLine("<ApplicationPackage SchemaVersion=""1.0"" AppVersion=""$PluginVersion"" Name=""XYD Toolkit"" Description=""XYD AutoCAD plugin"" Author=""XYD Studio"" ProductCode=""{9B3C2D1A-8F7E-6D5C-4B3A-2F1E0D9C8B7A}"">")
    [void]$xml.AppendLine("  <CompanyDetails Name=""XYD Studio"" />")
    foreach ($segment in $Segments) {
        Add-PackageComponentXml -Builder $xml -Name $segment.AppName -ModuleDir $segment.Id -SeriesMin $segment.SeriesMin -SeriesMax $segment.SeriesMax
    }
    [void]$xml.AppendLine("</ApplicationPackage>")

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [System.IO.File]::WriteAllText($Path, $xml.ToString(), [System.Text.UTF8Encoding]::new($false))
}

function Build-LegacySegment {
    param(
        [object]$Segment,
        [string]$MSBuildPath
    )

    $outDir = Join-Path $ArtifactsRoot $Segment.Id
    $objDir = Join-Path $RepoRoot ("artifacts\obj\" + $Segment.Id)
    $refDir = Get-NativeSafePath $Segment.RefDir
    Remove-DirectorySafe $outDir
    Remove-DirectorySafe $objDir
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    New-Item -ItemType Directory -Force -Path $objDir | Out-Null

    $args = @(
        $LegacyProject,
        "/t:Rebuild",
        "/p:Configuration=$Configuration",
        "/p:Platform=x64",
        "/p:TargetFrameworkVersion=$($Segment.TargetFrameworkVersion)",
        "/p:AcadRefDir=$refDir",
        "/p:AcadSeriesDefine=$($Segment.Defines)",
        "/p:OutDir=$outDir\",
        "/p:BaseIntermediateOutputPath=$objDir\",
        "/p:IntermediateOutputPath=$objDir\",
        "/v:minimal",
        "/nologo"
    )
    Invoke-Process -FilePath $MSBuildPath -Arguments $args
    return $outDir
}

function Build-Net8Segment {
    param([object]$Segment)

    $outDir = Join-Path $ArtifactsRoot $Segment.Id
    $objDir = Join-Path $RepoRoot ("artifacts\obj\" + $Segment.Id)
    $refDir = Get-NativeSafePath $Segment.RefDir
    Remove-DirectorySafe $outDir
    Remove-DirectorySafe $objDir
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    New-Item -ItemType Directory -Force -Path $objDir | Out-Null

    $args = @(
        "build",
        $Net8Project,
        "-c",
        $Configuration,
        "/p:Platform=x64",
        "/p:AcadNet8RefDir=$refDir",
        "/p:OutDir=$outDir\",
        "/p:BaseIntermediateOutputPath=$objDir\",
        "/p:IntermediateOutputPath=$objDir\",
        "/v:minimal",
        "--nologo"
    )
    Invoke-Process -FilePath "dotnet" -Arguments $args
    return $outDir
}

Write-Step "Resolving build tools"
$msbuild = Get-MSBuildPath
$iscc = $null
if (-not $SkipInstaller) {
    $iscc = Get-IsccPath
}

Write-Step "Resolving AutoCAD SDK references"
$ref2018 = Ensure-ObjectArxSdk `
    -Year 2018 `
    -Url "https://download.autodesk.com/esd/objectarx/2018/Autodesk_ObjectARX_2018_Win_64_and_32_Bit.sfx.exe" `
    -FileName "Autodesk_ObjectARX_2018_Win_64_and_32_Bit.sfx.exe" `
    -ProvidedRoot $ObjectArx2018Root

$ref2020 = Ensure-ObjectArxSdk `
    -Year 2020 `
    -Url "https://download.autodesk.com/esd/objectarx/2020/objectarx_for_autocad_2020_win_64_bit.sfx.exe" `
    -FileName "objectarx_for_autocad_2020_win_64_bit.sfx.exe" `
    -ProvidedRoot $ObjectArx2020Root

$ref2024 = Find-InstalledAutoCadRefDir @("R24.3", "R24.2", "R24.1", "R24.0")
if (-not $ref2024) {
    throw "AutoCAD 2021-2024 references were not found. Install AutoCAD 2024/2023/2022/2021 or pass a compatible reference directory through AcadRefDir manually."
}

$refNet8 = Resolve-Net8RefDir
if (-not $refNet8) {
    $message = "AutoCAD 2025/2026 .NET 8 references were not found. Autodesk gates ObjectARX 2025/2026 behind a license/reCAPTCHA page: $LatestObjectArxLicenseUrl. Download it there, then rerun the script; the script will find the SFX in Downloads/cache, or pass -ObjectArx2025Root/-ObjectArx2026Root."
    if (-not $ContinueWithoutNet8) {
        throw $message
    }
    Write-Warning $message
}

$segments = @(
    [pscustomobject]@{
        Id = "AutoCAD2018"
        AppName = "XYDSignTool2018"
        Runtime = "netfx"
        RefDir = $ref2018
        TargetFrameworkVersion = (Get-NetFxTargetingVersion @("v4.6", "v4.7.2", "v4.8"))
        Defines = "ACAD2018"
        SeriesMin = "R22.0"
        SeriesMax = "R22.0"
    },
    [pscustomobject]@{
        Id = "AutoCAD2019-2020"
        AppName = "XYDSignTool2019To2020"
        Runtime = "netfx"
        RefDir = $ref2020
        TargetFrameworkVersion = "v4.7.2"
        Defines = "ACAD2019_2020"
        SeriesMin = "R23.0"
        SeriesMax = "R23.1"
    },
    [pscustomobject]@{
        Id = "AutoCAD2021-2024"
        AppName = "XYDSignTool2021To2024"
        Runtime = "netfx"
        RefDir = $ref2024
        TargetFrameworkVersion = "v4.8"
        Defines = "ACAD2021_2024"
        SeriesMin = "R24.0"
        SeriesMax = "R24.3"
    }
)

if ($refNet8) {
    $segments += [pscustomobject]@{
        Id = "AutoCAD2025-2026"
        AppName = "XYDSignTool2025To2026"
        Runtime = "net8"
        RefDir = $refNet8
        TargetFrameworkVersion = ""
        Defines = "ACAD_NET8;ACAD2025_2026"
        SeriesMin = "R25.0"
        SeriesMax = "R25.1"
    }
}

Write-Step "Cleaning package output"
Remove-DirectorySafe $ArtifactsRoot
Remove-DirectorySafe $BundleRoot
New-Item -ItemType Directory -Force -Path $ArtifactsRoot | Out-Null
New-Item -ItemType Directory -Force -Path $BundleRoot | Out-Null

$builtSegments = @()
foreach ($segment in $segments) {
    Write-Step "Building $($segment.Id)"
    if ($segment.Runtime -eq "net8") {
        $buildOutput = Build-Net8Segment $segment
    } else {
        $buildOutput = Build-LegacySegment -Segment $segment -MSBuildPath $msbuild
    }

    $destination = Join-Path $BundleRoot ("Contents\" + $segment.Id)
    Copy-PluginPayload -BuildOutputDir $buildOutput -DestinationDir $destination
    $builtSegments += $segment
}

Write-Step "Writing PackageContents.xml"
Write-PackageContentsXml -Segments $builtSegments -Path (Join-Path $BundleRoot "PackageContents.xml")

$iconSource = Join-Path $RepoRoot "Installer\XYD_Toolkit.ico"
if (Test-Path -LiteralPath $iconSource) {
    Copy-Item -LiteralPath $iconSource -Destination (Join-Path $BundleRoot "XYD_Toolkit.ico") -Force
}

if (-not $SkipInstaller) {
    if (-not $refNet8) {
        throw "Refusing to build final installer without the AutoCAD2025-2026 payload. Pass -SkipInstaller for partial package validation."
    }

    Write-Step "Compiling Inno Setup installer"
    Invoke-Process -FilePath $iscc -Arguments @(
        "/DBundleBuildDir=$BundleRoot",
        "/DPluginVersion=$PluginVersion",
        "/DInstallerVersionLabel=$InstallerVersionLabel",
        $InstallerScript
    )
}

Write-Step "Done"
Write-Host "Bundle: $BundleRoot"
if (-not $SkipInstaller) {
    Write-Host "Installer output: $(Join-Path $RepoRoot 'Installer\Output')"
}
