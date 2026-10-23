[Setup]
AppId={{C7B63617-7CEF-49E0-AC21-D106C32994EB}
AppName=Local Audio Control
AppVersion=1.1.5
AppPublisher=Local Audio Control
DefaultDirName={localappdata}\Programs\LocalAudioControl
DefaultGroupName=Local Audio Control
PrivilegesRequired=lowest
MinVersion=10.0
OutputBaseFilename=LocalAudioControl-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\LocalAudioControl.exe
CloseApplications=yes
RestartApplications=no
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "build\LocalAudioControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\LocalAudioControl.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "build\Commands\*.exe"; DestDir: "{app}\Commands"; Flags: ignoreversion
Source: "build\LegacyCommands\*.exe"; DestDir: "{app}\LegacyCommands"; Flags: ignoreversion

[Icons]
Name: "{group}\Local Audio Control"; Filename: "{app}\LocalAudioControl.exe"; Parameters: "--settings"
Name: "{group}\Удалить Local Audio Control"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\LocalAudioControl.exe"; Parameters: "--tray"; Description: "Запустить Local Audio Control"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "LocalAudioControl"; Flags: uninsdeletevalue

[Code]
function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
  if not Result then
    MsgBox('Для Local Audio Control требуется Microsoft .NET Framework 4.8 или новее. Установите его с сайта Microsoft и повторите установку.', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\LocalAudioControl.exe')) then
    Exec(ExpandConstant('{app}\LocalAudioControl.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeUninstall(): Boolean;
var Code: Integer;
begin
  Result := True;
  if FileExists(ExpandConstant('{app}\LocalAudioControl.exe')) then
    Exec(ExpandConstant('{app}\LocalAudioControl.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then begin
    if not UninstallSilent then
      if MsgBox('Удалить также настройки Local Audio Control? Нажмите «Нет», чтобы сохранить избранные устройства и параметры.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(ExpandConstant('{localappdata}\LocalAudioControl'), True, True, True);
  end;
end;
