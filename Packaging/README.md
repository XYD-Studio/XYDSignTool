# XYDSignTool AutoCAD 2018-2026 Packaging

This folder contains the reproducible packaging flow for the multi-version
AutoCAD installer.

## Build

Run from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Packaging\Build-AutoCADPackage.ps1
```

The script builds these payloads:

- `AutoCAD2018` using ObjectARX 2018 references
- `AutoCAD2019-2020` using ObjectARX 2020 references
- `AutoCAD2021-2024` using an installed AutoCAD 2021-2024 reference directory
- `AutoCAD2025-2026` using ObjectARX 2025 or 2026 .NET 8 references

The final installer is written to:

```text
Installer\Output\XYD_Toolkit_V1.1_AutoCAD2018-2026_Setup.exe
```

## SDK Inputs

ObjectARX 2018 and 2020 are downloaded automatically from Autodesk's official
legacy direct links and cached under:

```text
%LOCALAPPDATA%\XYDSignTool\ObjectARX
```

Autodesk gates ObjectARX 2025/2026 behind a license agreement and reCAPTCHA on
the official SDK page:

```text
https://aps.autodesk.com/developer/overview/autocad-objectarx-sdk-licensing
```

The script will not bypass that challenge. Use either:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Packaging\Build-AutoCADPackage.ps1 -OpenSdkDownloadPage -WaitForSdkDownloadMinutes 15
```

or download/extract the SDK from Autodesk and pass its location:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Packaging\Build-AutoCADPackage.ps1 -ObjectArx2026Root "D:\SDKs\ObjectARX2026"
```

The script also searches `%USERPROFILE%\Downloads`, `%USERPROFILE%\Desktop`, and
the cache folder for official ObjectARX 2025/2026 SFX installers.

## Partial Validation

To validate only the .NET Framework payloads and bundle layout when the
2025/2026 SDK is not available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Packaging\Build-AutoCADPackage.ps1 -ContinueWithoutNet8 -SkipInstaller
```

This creates:

```text
Installer\Build\XYDSignTool.bundle
```
