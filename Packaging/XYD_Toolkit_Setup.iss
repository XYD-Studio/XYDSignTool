#ifndef BundleBuildDir
#define BundleBuildDir "..\Installer\Build\XYDSignTool.bundle"
#endif
#ifndef PluginVersion
#define PluginVersion "1.1.0"
#endif
#ifndef InstallerVersionLabel
#define InstallerVersionLabel "V1.1"
#endif

[Setup]
AppId={{9B3C2D1A-8F7E-6D5C-4B3A-2F1E0D9C8B7A}
AppName=XYD Toolkit AutoCAD Plugin
AppVersion={#PluginVersion}
AppPublisher=XYD Studio
AppPublisherURL=https://www.xy-d.top/
AppSupportURL=https://www.xy-d.top/
AppUpdatesURL=https://www.xy-d.top/
DefaultDirName={commonpf}\Autodesk\ApplicationPlugins\XYDSignTool.bundle
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir=..\Installer\Output
OutputBaseFilename=XYD_Toolkit_{#InstallerVersionLabel}_AutoCAD2018-2026_Setup
SetupIconFile=..\Installer\XYD_Toolkit.ico
UninstallDisplayIcon={app}\XYD_Toolkit.ico
Compression=lzma2/ultra64
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#BundleBuildDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
var
  CadPage: TWizardPage;
  CadInfoLabel: TNewStaticText;
  CadChecks: TNewCheckListBox;
  CadYears: array of Integer;
  CadSeries: array of String;
  CadItemCount: Integer;
  DetectedCadCount: Integer;

function HasInstalledAutoCADInRoot(RootKey: Integer; Series: String): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  KeyPath: String;
  Location: String;
begin
  Result := False;
  KeyPath := 'SOFTWARE\Autodesk\AutoCAD\' + Series;

  if not RegGetSubkeyNames(RootKey, KeyPath, Names) then
  begin
    exit;
  end;

  for I := 0 to GetArrayLength(Names) - 1 do
  begin
    if RegQueryStringValue(RootKey, KeyPath + '\' + Names[I], 'AcadLocation', Location) then
    begin
      if FileExists(AddBackslash(Location) + 'acad.exe') then
      begin
        Result := True;
        exit;
      end;
    end;
  end;
end;

function HasInstalledAutoCAD(Series: String): Boolean;
begin
  Result :=
    HasInstalledAutoCADInRoot(HKLM64, Series) or
    HasInstalledAutoCADInRoot(HKCU, Series);
end;

procedure AddVersion(Year: Integer; Series: String; Checked: Boolean);
var
  Index: Integer;
begin
  Index := CadChecks.AddCheckBox('AutoCAD ' + IntToStr(Year) + ' (' + Series + ')', '', 0, Checked, True, False, True, nil);
  SetArrayLength(CadYears, Index + 1);
  SetArrayLength(CadSeries, Index + 1);
  CadYears[Index] := Year;
  CadSeries[Index] := Series;
  CadItemCount := CadItemCount + 1;
end;

procedure AddDetectedVersion(Year: Integer; Series: String);
begin
  if HasInstalledAutoCAD(Series) then
  begin
    AddVersion(Year, Series, True);
    DetectedCadCount := DetectedCadCount + 1;
  end;
end;

procedure AddAllVersionsForManualSelection();
begin
  AddVersion(2018, 'R22.0', False);
  AddVersion(2019, 'R23.0', False);
  AddVersion(2020, 'R23.1', False);
  AddVersion(2021, 'R24.0', False);
  AddVersion(2022, 'R24.1', False);
  AddVersion(2023, 'R24.2', False);
  AddVersion(2024, 'R24.3', False);
  AddVersion(2025, 'R25.0', False);
  AddVersion(2026, 'R25.1', False);
end;

function IsYearSelected(Year: Integer): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to CadItemCount - 1 do
  begin
    if (CadYears[I] = Year) and CadChecks.Checked[I] then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function IsAnyYearSelected(): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to CadItemCount - 1 do
  begin
    if CadChecks.Checked[I] then
    begin
      Result := True;
      exit;
    end;
  end;
end;

function IsSegmentSelected(Segment: String): Boolean;
begin
  Result := False;
  if Segment = 'AutoCAD2018' then
  begin
    Result := IsYearSelected(2018);
  end
  else if Segment = 'AutoCAD2019-2020' then
  begin
    Result := IsYearSelected(2019) or IsYearSelected(2020);
  end
  else if Segment = 'AutoCAD2021-2024' then
  begin
    Result := IsYearSelected(2021) or IsYearSelected(2022) or IsYearSelected(2023) or IsYearSelected(2024);
  end
  else if Segment = 'AutoCAD2025-2026' then
  begin
    Result := IsYearSelected(2025) or IsYearSelected(2026);
  end;
end;

function ModuleDirForYear(Year: Integer): String;
begin
  if Year = 2018 then
  begin
    Result := 'AutoCAD2018';
  end
  else if (Year = 2019) or (Year = 2020) then
  begin
    Result := 'AutoCAD2019-2020';
  end
  else if (Year >= 2021) and (Year <= 2024) then
  begin
    Result := 'AutoCAD2021-2024';
  end
  else
  begin
    Result := 'AutoCAD2025-2026';
  end;
end;

function AppNameForYear(Year: Integer): String;
begin
  Result := 'XYDSignTool' + IntToStr(Year);
end;

function ComponentXmlForYear(Year: Integer; Series: String): String;
var
  ModuleDir: String;
begin
  ModuleDir := ModuleDirForYear(Year);
  Result :=
    '  <Components>' + #13#10 +
    '    <RuntimeRequirements OS="Win64" Platform="AutoCAD*" SeriesMin="' + Series + '" SeriesMax="' + Series + '" />' + #13#10 +
    '    <ComponentEntry AppName="' + AppNameForYear(Year) + '" ModuleName="./Contents/' + ModuleDir + '/XYDSignTool.dll" AppDescription="XYDSignTool AutoCAD plugin" LoadOnAutoCADStartup="True" LoadOnCommandInvocation="False" />' + #13#10 +
    '  </Components>' + #13#10;
end;

procedure DeleteUnselectedPayloads();
var
  ContentRoot: String;
begin
  ContentRoot := ExpandConstant('{app}\Contents');

  if not IsSegmentSelected('AutoCAD2018') then
    DelTree(ContentRoot + '\AutoCAD2018', True, True, True);

  if not IsSegmentSelected('AutoCAD2019-2020') then
    DelTree(ContentRoot + '\AutoCAD2019-2020', True, True, True);

  if not IsSegmentSelected('AutoCAD2021-2024') then
    DelTree(ContentRoot + '\AutoCAD2021-2024', True, True, True);

  if not IsSegmentSelected('AutoCAD2025-2026') then
    DelTree(ContentRoot + '\AutoCAD2025-2026', True, True, True);
end;

procedure WriteSelectedPackageContents();
var
  I: Integer;
  Xml: String;
  PackagePath: String;
begin
  Xml :=
    '<?xml version="1.0" encoding="utf-8"?>' + #13#10 +
    '<ApplicationPackage SchemaVersion="1.0" AppVersion="{#PluginVersion}" Name="XYD Toolkit" Description="XYD AutoCAD plugin" Author="XYD Studio" ProductCode="{9B3C2D1A-8F7E-6D5C-4B3A-2F1E0D9C8B7A}">' + #13#10 +
    '  <CompanyDetails Name="XYD Studio" />' + #13#10;

  for I := 0 to CadItemCount - 1 do
  begin
    if CadChecks.Checked[I] then
    begin
      Xml := Xml + ComponentXmlForYear(CadYears[I], CadSeries[I]);
    end;
  end;

  Xml := Xml + '</ApplicationPackage>' + #13#10;
  PackagePath := ExpandConstant('{app}\PackageContents.xml');
  if not SaveStringToFile(PackagePath, Xml, False) then
  begin
    RaiseException('Failed to write PackageContents.xml: ' + PackagePath);
  end;
end;

procedure InitializeWizard();
begin
  CadItemCount := 0;
  DetectedCadCount := 0;

  CadPage := CreateCustomPage(
    wpSelectDir,
    'Select AutoCAD versions',
    'Choose the installed AutoCAD versions that should load XYD Toolkit.');

  CadInfoLabel := TNewStaticText.Create(CadPage);
  CadInfoLabel.Parent := CadPage.Surface;
  CadInfoLabel.Left := 0;
  CadInfoLabel.Top := 0;
  CadInfoLabel.Width := CadPage.SurfaceWidth;
  CadInfoLabel.Caption := 'Detected AutoCAD 2018-2026 installations are selected by default.';

  CadChecks := TNewCheckListBox.Create(CadPage);
  CadChecks.Parent := CadPage.Surface;
  CadChecks.Left := 0;
  CadChecks.Top := CadInfoLabel.Top + CadInfoLabel.Height + ScaleY(10);
  CadChecks.Width := CadPage.SurfaceWidth;
  CadChecks.Height := CadPage.SurfaceHeight - CadChecks.Top;

  AddDetectedVersion(2018, 'R22.0');
  AddDetectedVersion(2019, 'R23.0');
  AddDetectedVersion(2020, 'R23.1');
  AddDetectedVersion(2021, 'R24.0');
  AddDetectedVersion(2022, 'R24.1');
  AddDetectedVersion(2023, 'R24.2');
  AddDetectedVersion(2024, 'R24.3');
  AddDetectedVersion(2025, 'R25.0');
  AddDetectedVersion(2026, 'R25.1');

  if DetectedCadCount = 0 then
  begin
    CadInfoLabel.Caption := 'No AutoCAD 2018-2026 installation was detected. Select versions manually.';
    AddAllVersionsForManualSelection();
  end;
end;

function IsAutoCADRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden ' +
      '-Command "if (Get-Process -Name acad -ErrorAction SilentlyContinue) { exit 10 } else { exit 0 }"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := ResultCode = 10;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsAutoCADRunning() then
  begin
    Result := 'AutoCAD is running. Close all AutoCAD windows, then click Install again.';
  end;
end;

procedure RemoveExistingBundles();
var
  ProgramFilesBundle: String;
  ProgramDataBundle: String;
begin
  ProgramFilesBundle := ExpandConstant('{commonpf}\Autodesk\ApplicationPlugins\XYDSignTool.bundle');
  ProgramDataBundle := ExpandConstant('{commonappdata}\Autodesk\ApplicationPlugins\XYDSignTool.bundle');

  if DirExists(ProgramFilesBundle) then
  begin
    if not DelTree(ProgramFilesBundle, True, True, True) then
      RaiseException('Unable to remove the previous plug-in from: ' + ProgramFilesBundle);
  end;

  if DirExists(ProgramDataBundle) then
  begin
    if not DelTree(ProgramDataBundle, True, True, True) then
      RaiseException('Unable to replace the previous plug-in in: ' + ProgramDataBundle);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = CadPage.ID then
  begin
    if not IsAnyYearSelected() then
    begin
      MsgBox('Select at least one AutoCAD version.', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RemoveExistingBundles();
  end
  else if CurStep = ssPostInstall then
  begin
    DeleteUnselectedPayloads();
    WriteSelectedPackageContents();
    if not WizardSilent() then
    begin
      MsgBox('XYD Toolkit installation is complete.' + #13#10 + #13#10 +
        'Restart AutoCAD. If the ribbon tab does not appear, run command XYD_UI.',
        mbInformation, MB_OK);
    end;
  end;
end;
