param([string]$PluginPath)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskBuild = 'D:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
$taskCad = 'D:\Program Files\autodesk\AutoCAD 2024\AutoCAD 2024'
if (-not $PluginPath) {
    & $taskBuild (Join-Path $taskRepo 'XYDSignTool.csproj') /t:Build /p:Configuration=Debug /p:Platform=x64 /verbosity:minimal /nologo
    if ($LASTEXITCODE) { throw 'Plugin build failed' }
    $PluginPath = Join-Path $taskRepo 'bin/x64/Debug/XYDSignTool.dll'
}
$taskPlugin = (Resolve-Path -LiteralPath $PluginPath).Path
& $taskBuild (Join-Path $PSScriptRoot 'UpgradeTests.csproj') /t:Build "/p:PluginPath=$taskPlugin" /verbosity:minimal /nologo
if ($LASTEXITCODE) { throw 'Harness build failed' }
$taskOutput = Join-Path $taskRepo 'artifacts/upgrade-regression'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
$taskResults = Join-Path $taskOutput 'results.txt'
if (Test-Path -LiteralPath $taskResults) { Remove-Item -LiteralPath $taskResults }
$taskTrust = Join-Path $env:ProgramFiles 'Autodesk/ApplicationPlugins/XYDSignTool.bundle'
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskTrust ('Verification_' + [guid]::NewGuid().ToString('N'))))
if (-not $taskStage.StartsWith([IO.Path]::GetFullPath($taskTrust) + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test path' }
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskSlash = $taskStage.Replace('\','/')
$taskPluginName = [IO.Path]::GetFileName($taskPlugin)
$taskScript = @"
NETLOAD
"$taskSlash/$taskPluginName"
NETLOAD
"$taskSlash/UpgradeTests.dll"
XYD_UPGRADE_TESTS
XYD_SCALE_FIXTURE
_.UCS
_Z
35
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_MSCALE
2
XYD_SCALE_VERIFY
_.U
XYD_SCALE_ORIGINAL
XYD_MSCALE
_ALL

2
XYD_SCALE_VERIFY
_.U
XYD_SCALE_ORIGINAL
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_MSCALE

XYD_SCALE_ORIGINAL
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_MSCALE
(command)
XYD_SCALE_ORIGINAL
_.QUIT
_Y
"@
$taskOldRoot = $env:XYD_UPGRADE_ROOT
try {
    Copy-Item -LiteralPath $taskPlugin,(Join-Path $taskOutput 'harness/UpgradeTests.dll') -Destination $taskStage
    Copy-Item -LiteralPath (Join-Path $taskRepo 'Blocks/XYD_TitleBlocks.dwg') -Destination (Join-Path $taskStage 'input.dwg')
    [IO.File]::WriteAllText((Join-Path $taskOutput 'tested-plugin.sha256'), (Get-FileHash -LiteralPath $taskPlugin -Algorithm SHA256).Hash)
    [IO.File]::WriteAllText((Join-Path $taskStage 'run.scr'), $taskScript + [Environment]::NewLine, [Text.Encoding]::ASCII)
    $env:XYD_UPGRADE_ROOT = $taskRepo
    $taskProcess = Start-Process -FilePath (Join-Path $taskCad 'accoreconsole.exe') -WorkingDirectory $taskStage -ArgumentList @('/i',('"'+(Join-Path $taskStage 'input.dwg')+'"'),'/s',('"'+(Join-Path $taskStage 'run.scr')+'"'),'/l','en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskOutput 'console.txt') -RedirectStandardError (Join-Path $taskOutput 'errors.txt')
    $taskExited=$false
    for ($taskWait=0; $taskWait -lt 50; $taskWait++) { if ($taskProcess.WaitForExit(1000)) { $taskExited=$true; break } }
    if (-not $taskExited) { Stop-Process -Id $taskProcess.Id; $null=$taskProcess.WaitForExit(5000); throw 'Upgrade regression timeout' }
    $taskText = Get-Content -LiteralPath $taskResults -Raw; Write-Output $taskText
    if ($taskText -match '(?m)^FAIL\b' -or ([regex]::Matches($taskText,'(?m)^PASS\b')).Count -lt 10) { throw 'Upgrade regression failed or incomplete' }
} finally {
    $env:XYD_UPGRADE_ROOT=$taskOldRoot
    Remove-Item -LiteralPath $taskStage -Recurse -Force
}
