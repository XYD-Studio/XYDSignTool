param([string]$AutoCadRoot = 'D:\Program Files\autodesk\AutoCAD 2024\AutoCAD 2024')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskBuild = 'D:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
& $taskBuild (Join-Path $taskRepo 'XYDSignTool.csproj') /t:Build /p:Configuration=Release /p:Platform=x64 "/p:AcadRefDir=$AutoCadRoot" /p:OutputPath=artifacts/ribbon-regression/plugin/ /p:BaseIntermediateOutputPath=artifacts/ribbon-regression/obj-plugin/ /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
& $taskBuild (Join-Path $PSScriptRoot 'RibbonRegressionTests.csproj') /t:Build "/p:AcadRefDir=$AutoCadRoot" /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
$taskResults = Join-Path $taskRepo 'artifacts/ribbon-regression/results.txt'
if (Test-Path -LiteralPath $taskResults) { Remove-Item -LiteralPath $taskResults }
# Program Files is trusted by AutoCAD. No changes to SECURELOAD or TRUSTEDPATHS.
$taskTrustRoot = Join-Path $env:ProgramFiles 'Autodesk/ApplicationPlugins/XYDSignTool.bundle'
if (-not (Test-Path -LiteralPath $taskTrustRoot)) { throw 'Install the plugin before running this host regression.' }
$taskStage = [IO.Path]::GetFullPath((Join-Path $taskTrustRoot ('Verification_' + [Guid]::NewGuid().ToString('N'))))
if (-not $taskStage.StartsWith([IO.Path]::GetFullPath($taskTrustRoot) + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid verification path' }
New-Item -ItemType Directory -Path $taskStage | Out-Null
$taskSlash = $taskStage.Replace('\','/')
$taskScript = @"
NETLOAD
"$taskSlash/XYDSignTool.dll"
NETLOAD
"$taskSlash/RibbonRegressionTests.dll"
XYD_RIBBON_REGRESSION
QUIT
Y
"@
$taskOldTestRoot = $env:XYD_RIBBON_TEST_ROOT
try {
    Copy-Item -LiteralPath (Join-Path $taskRepo 'artifacts/ribbon-regression/plugin/XYDSignTool.dll'),(Join-Path $taskRepo 'artifacts/ribbon-regression/harness/RibbonRegressionTests.dll') -Destination $taskStage
    Copy-Item -LiteralPath (Join-Path $taskRepo 'Blocks/XYD_TitleBlocks.dwg') -Destination (Join-Path $taskStage 'input.dwg')
    [IO.File]::WriteAllText((Join-Path $taskStage 'run.scr'), $taskScript + [Environment]::NewLine, [Text.Encoding]::ASCII)
    $env:XYD_RIBBON_TEST_ROOT = $taskRepo
    $taskProcess = Start-Process -FilePath (Join-Path $AutoCadRoot 'accoreconsole.exe') -WorkingDirectory $taskStage -ArgumentList @('/i',('"' + (Join-Path $taskStage 'input.dwg') + '"'),'/s',('"' + (Join-Path $taskStage 'run.scr') + '"'),'/l','en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRepo 'artifacts/ribbon-regression/console.txt') -RedirectStandardError (Join-Path $taskRepo 'artifacts/ribbon-regression/errors.txt')
    if (-not $taskProcess.WaitForExit(50000)) { Stop-Process -Id $taskProcess.Id; throw 'Ribbon regression timed out' }
    if (-not (Test-Path -LiteralPath $taskResults)) { throw 'No results; see artifacts/ribbon-regression/console.txt' }
    $taskText = Get-Content -LiteralPath $taskResults -Raw
    Write-Output $taskText
    if ($taskText -match '(?m)^FAIL\b') { throw 'Ribbon regression failed' }
} finally {
    $env:XYD_RIBBON_TEST_ROOT = $taskOldTestRoot
    # Only this newly created staging directory is removed; installed payloads remain intact.
    Remove-Item -LiteralPath $taskStage -Recurse -Force
}
