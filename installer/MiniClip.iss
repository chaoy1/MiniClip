#ifndef PublishDir
  #error PublishDir must be passed by the build script.
#endif
#ifndef OutputDir
  #error OutputDir must be passed by the build script.
#endif
#ifndef AppVersion
  #error AppVersion must be passed by the build script.
#endif

[Setup]
AppId={{6F083A51-5D87-4F8B-8812-57BB56451F65}
AppName=MiniClip
AppVersion={#AppVersion}
AppVerName=MiniClip {#AppVersion}
AppPublisher=MiniClip
AppMutex=Local\MiniClip.SingleInstance.v1
DefaultDirName={localappdata}\Programs\MiniClip
DefaultGroupName=MiniClip
DisableDirPage=no
UsePreviousAppDir=yes
UsePreviousTasks=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern dynamic
SetupIconFile=..\src\MiniClip\Assets\miniclip.ico
UninstallDisplayIcon={app}\MiniClip.exe
OutputDir={#OutputDir}
OutputBaseFilename=MiniClip-Setup-{#AppVersion}-x64
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
ConfirmUninstall=确定卸载 %1 吗？卸载会永久删除 MiniClip 的全部历史记录、设置和日志。

[Tasks]
Name: "autostart"; Description: "开机自动启动 MiniClip"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MiniClip"; Filename: "{app}\MiniClip.exe"; WorkingDir: "{app}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MiniClip"; ValueData: """{app}\MiniClip.exe"""; Tasks: autostart

[Run]
Filename: "{app}\MiniClip.exe"; Description: "立即运行 MiniClip"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; This is MiniClip's dedicated per-user data directory, never the user-selected {app}.
Type: filesandordirs; Name: "{localappdata}\MiniClip"

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  StartupApprovalKey = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run';
  StartupApproval32Key = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32';

procedure ClearStartupApproval();
begin
  RegDeleteValue(HKCU, StartupApprovalKey, 'MiniClip');
  RegDeleteValue(HKCU, StartupApproval32Key, 'MiniClip');
end;

procedure InitializeWizard();
var
  ExistingCommand: String;
begin
  { The app's settings page can change startup after installation. Reflect the live value. }
  if RegQueryStringValue(HKCU, StartupKey, 'MiniClip', ExistingCommand) and
     (Trim(ExistingCommand) <> '') then
    WizardSelectTasks('autostart');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { A task-manager-disabled startup value must not override this choice. }
    ClearStartupApproval();
    { An unchecked task is a real choice, including when upgrading a prior install. }
    if not WizardIsTaskSelected('autostart') then
      RegDeleteValue(HKCU, StartupKey, 'MiniClip');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  { Also remove startup when it was enabled later from MiniClip's settings page. }
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(HKCU, StartupKey, 'MiniClip');
    ClearStartupApproval();
  end;
end;
