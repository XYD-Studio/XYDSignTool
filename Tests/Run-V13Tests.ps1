param([string]$AutoCadRoot = 'D:\Program Files\autodesk\AutoCAD 2024\AutoCAD 2024')
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskMsbuild = 'D:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'
& $taskMsbuild (Join-Path $taskRepo 'XYDSignTool.csproj') /t:Build /p:Configuration=Release /p:Platform=x64 "/p:AcadRefDir=$AutoCadRoot" /p:OutputPath=artifacts/v13-check/net48/ /p:BaseIntermediateOutputPath=artifacts/v13-check/obj48/ /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed' }
& $taskMsbuild (Join-Path $PSScriptRoot 'V13Tests.csproj') /t:Build "/p:AcadRefDir=$AutoCadRoot" /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
$taskStage = Join-Path $env:TEMP ('XYDV13Validation_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskStage | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRepo 'artifacts/v13-check/net48/XYDSignTool.dll'),(Join-Path $taskRepo 'artifacts/v13-tests/V13Tests.dll') -Destination $taskStage
$taskSlash = $taskStage.Replace('\','/')
$taskScript = @"
(setq xyd_old_security (getvar "SECURELOAD"))
SECURELOAD
0
NETLOAD
"$taskSlash/XYDSignTool.dll"
NETLOAD
"$taskSlash/V13Tests.dll"
(setvar "SECURELOAD" xyd_old_security)
XYD_V13_INSPECT
XYD_V13_TEST
XYD_V13_LINE_FIXTURE
(sssetfirst nil (ssget "_X" '((0 . "LINE"))))
XYD_LINE2PL
0.01
XYD_V13_LINE_VERIFY
_.U
XYD_V13_UNDO_VERIFY
XYD_LINE2PL
0
_ALL

XYD_V13_LINE_VERIFY
_.U
XYD_V13_UNDO_VERIFY
QUIT
Y
"@
[IO.File]::WriteAllText((Join-Path $taskStage 'run.scr'), $taskScript + [Environment]::NewLine, [Text.Encoding]::ASCII)
$taskResults = Join-Path $taskRepo 'artifacts/v13-tests/results.txt'
if (Test-Path -LiteralPath $taskResults) { Remove-Item -LiteralPath $taskResults }
$env:XYD_V13_TEST_ROOT = $taskRepo
$taskTolerance = Join-Path $env:APPDATA 'XYDSignTool/LineTolerance.txt'
$taskPreviousTolerance = if (Test-Path -LiteralPath $taskTolerance) { [IO.File]::ReadAllBytes($taskTolerance) } else { $null }
$taskProcess = Start-Process -FilePath (Join-Path $AutoCadRoot 'accoreconsole.exe') -ArgumentList @('/s', ('"' + (Join-Path $taskStage 'run.scr') + '"'), '/l', 'en-US') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $taskStage 'console.txt') -RedirectStandardError (Join-Path $taskStage 'errors.txt')
try { if (-not $taskProcess.WaitForExit(55000)) { Stop-Process -Id $taskProcess.Id; throw "AutoCAD test timeout. Logs: $taskStage" } }
finally { if ($null -ne $taskPreviousTolerance) { [IO.File]::WriteAllBytes($taskTolerance, $taskPreviousTolerance) } elseif (Test-Path -LiteralPath $taskTolerance) { Remove-Item -LiteralPath $taskTolerance } }
if (-not (Test-Path -LiteralPath $taskResults)) { throw "No test results. Logs: $taskStage" }
$taskText = Get-Content -LiteralPath $taskResults -Raw
Write-Output $taskText
if ($taskText -match 'FAIL') { throw "Tests failed. Logs: $taskStage" }
