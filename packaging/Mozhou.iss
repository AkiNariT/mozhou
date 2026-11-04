#ifndef TestNamespace
  #define ProductKey "Mozhou"
  #define DisplayName "墨舟"
  #define Extension ".mozhou"
#else
  #define ProductKey "MozhouInstallerTest"
  #define DisplayName "MozhouInstallerTest"
  #define Extension ".mozhou-installertest"
#endif
#define ProgId ProductKey + ".Document"

[Setup]
AppId={#ProductKey}.Desktop
AppName={#DisplayName}
AppVersion={#AppVersion}
AppPublisher=AkiNariT
AppPublisherURL=https://github.com/AkiNariT/mozhou
AppUpdatesURL=https://github.com/AkiNariT/mozhou/releases
DefaultDirName={localappdata}\Programs\{#ProductKey}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename={#ProductKey}-Setup-{#AppVersion}-x64
SetupIconFile=..\Mozhou.Desktop\Assets\Mozhou.ico
UninstallDisplayIcon={app}\Mozhou.exe
UninstallDisplayName={#DisplayName}
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#DisplayName}
ChangesAssociations=yes
AppMutex=Local\{#ProductKey}.Desktop.Running
SetupMutex=Local\{#ProductKey}.Desktop.Setup
CloseApplications=yes
RestartApplications=no
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes

[Languages]
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "mozhou-installed.marker"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userdesktop}\{#DisplayName}"; Filename: "{app}\Mozhou.exe"; WorkingDir: "{app}"
Name: "{userprograms}\{#DisplayName}"; Filename: "{app}\Mozhou.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueData: "墨舟作品"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueData: """{app}\Mozhou.exe"",0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueData: """{app}\Mozhou.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\{#Extension}\OpenWithProgids"; ValueType: string; ValueName: "{#ProgId}"; ValueData: ""; Flags: uninsdeletevalue uninsdeletekeyifempty
; Never edit Explorer's protected UserChoice. Only supply the first default when none exists.
Root: HKCU; Subkey: "Software\Classes\{#Extension}"; ValueType: string; ValueData: "{#ProgId}"; Check: CanSupplyDefault
; Record ownership on every install, including upgrades that leave a pending import untouched.
Root: HKCU; Subkey: "Software\{#ProductKey}"; ValueType: none; ValueName: "LegacyPortablePath"; Flags: uninsdeletevalue uninsdeletekeyifempty
Root: HKCU; Subkey: "Software\{#ProductKey}"; ValueType: string; ValueName: "LegacyPortablePath"; ValueData: "{code:SelectedLegacyDir}"; Check: HasSelectedLegacyDir
Root: HKCU; Subkey: "Software\{#ProductKey}"; ValueType: none; ValueName: "LegacyPortablePath"; Flags: deletevalue; Check: ClearSkippedLegacyDir

[Run]
Filename: "{app}\Mozhou.exe"; Description: "启动墨舟"; Flags: nowait postinstall skipifsilent

[Code]
const
  ProductReg = 'Software\{#ProductKey}';
  ExtensionReg = 'Software\Classes\{#Extension}';
  UninstallReg = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductKey}.Desktop_is1';
var
  LegacyPage: TInputQueryWizardPage;
  LegacyCandidates: TStringList;
  IsUpgrade: Boolean;

function InitializeSetup: Boolean;
var
  InstalledText: String;
  InstalledVersion, SetupVersion: Int64;
begin
  Result := True;
  if RegQueryStringValue(HKCU, UninstallReg, 'DisplayVersion', InstalledText) and
     StrToVersion(InstalledText, InstalledVersion) and StrToVersion('{#AppVersion}', SetupVersion) then
    if ComparePackedVersion(InstalledVersion, SetupVersion) > 0 then begin
      SuppressibleMsgBox('当前安装的墨舟版本为 ' + InstalledText +
        '。请使用同版本或更新的安装包。', mbError, MB_OK, IDOK);
      Result := False;
    end;
end;

function CanSupplyDefault: Boolean;
var
  Current: String;
begin
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{#Extension}\UserChoice',
      'ProgId', Current) and (Current <> '') then begin
    Result := False;
    Exit;
  end;
  Result := (not RegQueryStringValue(HKCR, '{#Extension}', '', Current)) or (Current = '');
end;

function IsLegacyDir(const Dir: String): Boolean;
begin
  Result := (Dir <> '') and FileExists(AddBackslash(Dir) + 'Mozhou.exe') and
    (not FileExists(AddBackslash(Dir) + 'mozhou-installed.marker')) and
    (DirExists(AddBackslash(Dir) + 'Library') or
     FileExists(AddBackslash(Dir) + 'bookshelf.json') or
     FileExists(AddBackslash(Dir) + 'mozhou-settings.json'));
end;

procedure AddCandidate(Dir: String);
begin
  if not IsLegacyDir(Dir) then Exit;
  Dir := RemoveBackslashUnlessRoot(ExpandFileName(Dir));
  if LegacyCandidates.IndexOf(Dir) < 0 then LegacyCandidates.Add(Dir);
end;

procedure BrowseLegacy(Sender: TObject);
var
  Dir: String;
begin
  Dir := LegacyPage.Values[0];
  if BrowseForFolder('选择旧版墨舟所在的文件夹', Dir, False) then
    LegacyPage.Values[0] := Dir;
end;

procedure InitializeWizard;
var
  SourceDir, Previous, Description: String;
  Browse: TNewButton;
begin
  IsUpgrade := RegKeyExists(HKCU, UninstallReg);
  LegacyCandidates := TStringList.Create;
  if not IsUpgrade then begin
    SourceDir := ExpandConstant('{src}');
    AddCandidate(SourceDir);
    if RegQueryStringValue(HKCU, ProductReg, 'PortablePath', Previous) then AddCandidate(Previous);
    AddCandidate(AddBackslash(SourceDir) + 'Mozhou-0.7.0');
    AddCandidate(AddBackslash(SourceDir) + 'Mozhou-Windows-0.7.0');
  end;

  Description := '如需沿用旧书架、设置或 Library 中的作品，请选择旧版 Mozhou.exe 所在的文件夹。'
    + '留空即可跳过，作品会保留在原位置。';
  if LegacyCandidates.Count > 1 then
    Description := '检测到多份旧版资料。请确认下方的来源，或浏览选择另一份。清空即可跳过。';
  LegacyPage := CreateInputQueryPage(wpWelcome, '沿用便携版书架（可选）',
    '选择旧版书架与设置的来源', Description);
  LegacyPage.Add('旧版墨舟所在的文件夹', False);
  if LegacyCandidates.Count > 0 then LegacyPage.Values[0] := LegacyCandidates[0];
  Browse := TNewButton.Create(LegacyPage);
  Browse.Parent := LegacyPage.Surface;
  Browse.Caption := '浏览…';
  Browse.Width := WizardForm.NextButton.Width;
  Browse.Height := WizardForm.NextButton.Height;
  Browse.Left := LegacyPage.SurfaceWidth - Browse.Width;
  Browse.Top := LegacyPage.Edits[0].Top - 1;
  LegacyPage.Edits[0].Width := Browse.Left - ScaleX(8);
  Browse.OnClick := @BrowseLegacy;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = LegacyPage.ID) and (IsUpgrade or (LegacyCandidates.Count = 1));
end;

function SelectedLegacyDir(Param: String): String;
begin
  Result := Trim(LegacyPage.Values[0]);
end;

function HasSelectedLegacyDir: Boolean;
begin
  Result := (not IsUpgrade) and IsLegacyDir(SelectedLegacyDir(''));
end;

function ClearSkippedLegacyDir: Boolean;
begin
  Result := (not IsUpgrade) and (SelectedLegacyDir('') = '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = LegacyPage.ID then
    if (SelectedLegacyDir('') <> '') and not HasSelectedLegacyDir then begin
      MsgBox('请选择含 Mozhou.exe 和旧版 Library 文件夹、书架或设置的目录，或清空后继续。', mbError, MB_OK);
      Result := False;
    end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if IsLegacyDir(ExpandConstant('{app}')) then
    Result := '目标目录中有便携版墨舟。请保留原目录，将安装版放在独立的程序目录。';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Current: String;
begin
  if CurUninstallStep = usPostUninstall then begin
    if RegQueryStringValue(HKCU, ExtensionReg, '', Current) and
        (CompareText(Current, '{#ProgId}') = 0) then
      RegDeleteValue(HKCU, ExtensionReg, '');
    RegDeleteKeyIfEmpty(HKCU, ExtensionReg);
  end;
end;
