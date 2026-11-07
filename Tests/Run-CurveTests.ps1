param([string]$AutoCadRoot = 'D:\Program Files\autodesk\AutoCAD 2024\AutoCAD 2024', [string]$PluginPath)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskBuild = 'D:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
if (-not $PluginPath) {
    & $taskBuild (Join-Path $taskRepo 'XYDSignTool.csproj') /t:Build /p:Configuration=Release /p:Platform=x64 "/p:AcadRefDir=$AutoCadRoot" /p:OutputPath=artifacts/curve-regression/plugin/ /p:BaseIntermediateOutputPath=artifacts/curve-regression/obj-plugin/ /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
    $PluginPath = Join-Path $taskRepo 'artifacts/curve-regression/plugin/XYDSignTool.dll'
}
$taskPluginDll = (Resolve-Path -LiteralPath $PluginPath).Path
& $taskBuild (Join-Path $PSScriptRoot 'CurvePolylineTests.csproj') /t:Build "/p:AcadRefDir=$AutoCadRoot" /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
$taskMutex = [Threading.Mutex]::new($false, 'Local\XYDSignToolCurveRegression')
if (-not $taskMutex.WaitOne(0)) { $taskMutex.Dispose(); throw 'Another curve regression is using the shared result/settings files.' }
$taskResults = Join-Path $taskRepo 'artifacts/curve-regression/results.txt'
if (Test-Path -LiteralPath $taskResults) { Remove-Item -LiteralPath $taskResults }
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
"$taskSlash/CurvePolylineTests.dll"
XYD_CURVE_REGRESSION
XYD_CURVE_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_LINE2PL
Z
F
0.001
0.01
XYD_CURVE_VERIFY
_.U
XYD_CURVE_UNDO_VERIFY
XYD_LINE2PL
Z
0
_ALL

XYD_CURVE_VERIFY
_.U
XYD_CURVE_UNDO_VERIFY
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_LINE2PL

XYD_CURVE_FLAT_VERIFY
_.U
XYD_CURVE_UNDO_VERIFY
XYD_METADATA_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_LINE2PL
D
0.01
XYD_METADATA_VERIFY
_.U
XYD_METADATA_UNDO_VERIFY
XYD_LINE2PL

_ALL

XYD_METADATA_DISCARD_VERIFY
_.U
XYD_METADATA_UNDO_VERIFY
XYD_PL_JOIN_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_LINE2PL
D
D
Z
Z
F


XYD_PL_JOIN_VERIFY
_.U
XYD_PL_JOIN_UNDO_VERIFY
XYD_BREAK_REGRESSION
XYD_BREAK_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
5,0,0
XYD_BREAK_VERIFY2
_.U
XYD_BREAK_UNDO_VERIFY
(sssetfirst nil nil)
XYD_PLBREAK
_L
M
8,0,0
8,0,0
2,0,0
U
3,0,0

XYD_BREAK_VERIFY3
_.U
XYD_BREAK_UNDO_VERIFY
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
M
4,0,0
S
7,0,0
XYD_BREAK_VERIFY2
_.U
XYD_BREAK_UNDO_VERIFY
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
0,0,0
20,0,0

XYD_BREAK_EMPTY_VERIFY
XYD_BREAK_CLOSED_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
5,0,0
XYD_BREAK_CLOSED_VERIFY1
_.U
XYD_BREAK_CLOSED_UNDO
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
M
5,0,0
10,5,0

XYD_BREAK_CLOSED_VERIFY2
_.U
XYD_BREAK_CLOSED_UNDO
XYD_BREAK_3D_FIXTURE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
5,0,5.5
XYD_BREAK_3D_VERIFY
_.U
XYD_BREAK_3D_UNDO
XYD_BREAK_FIXTURE
XYD_BREAK_UCS
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
5,-2,0
XYD_BREAK_UCS_VERIFY
_.U
XYD_BREAK_UNDO_VERIFY
XYD_BREAK_UCS_RESTORE
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
M
4,0,0
(command)
XYD_BREAK_EMPTY_VERIFY
XYD_BREAK_LOCK
(sssetfirst nil (ssget "_X" '((410 . "Model"))))
XYD_PLBREAK
XYD_BREAK_EMPTY_VERIFY
QUIT
Y
"@
$taskOldRoot = $env:XYD_CURVE_TEST_ROOT
$taskSettings = Join-Path $env:APPDATA 'XYDSignTool'
$taskSavedSettings = @{}
foreach ($taskName in @('LineTolerance.txt','CurveTolerance.txt')) {
    $taskFile = Join-Path $taskSettings $taskName
    $taskSavedSettings[$taskName] = if (Test-Path -LiteralPath $taskFile) { [IO.File]::ReadAllBytes($taskFile) } else { $null }
}
try {
    Copy-Item -LiteralPath $taskPluginDll,(Join-Path $taskRepo 'artifacts/curve-regression/harness/CurvePolylineTests.dll') -Destination $taskStage
    [IO.File]::WriteAllText((Join-Path $taskRepo 'artifacts/curve-regression/tested-plugin.sha256'), (Get-FileHash -LiteralPath $taskPluginDll -Algorithm SHA256).Hash)
    Copy-Item -LiteralPath (Join-Path $taskRepo 'Blocks/XYD_TitleBlocks.dwg') -Destination (Join-Path $taskStage 'input.dwg')
    [IO.File]::WriteAllText((Join-Path $taskStage 'run.scr'), $taskScript + [Environment]::NewLine, [Text.Encoding]::ASCII)
    $env:XYD_CURVE_TEST_ROOT = $taskRepo
    $taskProcess = Start-Process -FilePath (Join-Path $AutoCadRoot 'accoreconsole.exe') -WorkingDirectory $taskStage -ArgumentList @('/i',('"' + (Join-Path $taskStage 'input.dwg') + '"'),'/s',('"' + (Join-Path $taskStage 'run.scr') + '"'),'/l','en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskRepo 'artifacts/curve-regression/console.txt') -RedirectStandardError (Join-Path $taskRepo 'artifacts/curve-regression/errors.txt')
    if (-not $taskProcess.WaitForExit(50000)) { Stop-Process -Id $taskProcess.Id; $null = $taskProcess.WaitForExit(5000); throw 'Curve regression timed out' }
    if (-not (Test-Path -LiteralPath $taskResults)) { throw 'No results; see artifacts/curve-regression/console.txt' }
    $taskText = Get-Content -LiteralPath $taskResults -Raw
    Write-Output $taskText
    if ($taskText -match '(?m)^FAIL\b' -or ([regex]::Matches($taskText, '(?m)^PASS\b')).Count -lt 57) { throw 'Curve regression failed or incomplete' }
} finally {
    $env:XYD_CURVE_TEST_ROOT = $taskOldRoot
    foreach ($taskName in $taskSavedSettings.Keys) {
        $taskFile = Join-Path $taskSettings $taskName
        if ($null -eq $taskSavedSettings[$taskName]) { if (Test-Path -LiteralPath $taskFile) { Remove-Item -LiteralPath $taskFile } }
        else { [IO.File]::WriteAllBytes($taskFile, $taskSavedSettings[$taskName]) }
    }
    # Path was verified above; only this newly created test directory is removed.
    for ($taskAttempt = 0; $taskAttempt -lt 3; $taskAttempt++) {
        try { Remove-Item -LiteralPath $taskStage -Recurse -Force; break }
        catch { if ($taskAttempt -eq 2) { Write-Warning "Test staging cleanup deferred: $taskStage" } else { Start-Sleep -Milliseconds 500 } }
    }
    $taskMutex.ReleaseMutex(); $taskMutex.Dispose()
}
