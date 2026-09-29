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
; 默认装到用户自己的目录：不需要管理员权限，而且该目录用户可写。
; 这一点是必须的——数据要落在程序目录下的 data\，就需要写权限；装到
; C:\Program Files 这类受保护目录时普通用户写不进去，程序会回退到
; %LOCALAPPDATA%\MiniClip 并给出托盘提示（见 src 里的 AppPaths）。
DefaultDirName={localappdata}\Programs\MiniClip
DefaultGroupName=MiniClip
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousTasks=no
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern dynamic
SetupIconFile=..\src\MiniClip\Assets\miniclip.ico
UninstallDisplayIcon={app}\MiniClip.exe
UninstallDisplayName=MiniClip {#AppVersion}
OutputDir={#OutputDir}
OutputBaseFilename=MiniClip-Setup-{#AppVersion}-x64
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
; 卸载时也要删掉数据目录，所以先让程序把文件句柄放掉。
; （程序自身在 [Code] 里退出，这里只负责进程层面的等待。）
ChangesAssociations=no

[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
; 把默认的“是否卸载”换成本项目的事实：卸载会删掉历史，需要提前说清。
ConfirmUninstall=确定卸载 %1 吗？%n%n这会删除程序，以及安装目录下的全部数据（历史记录、设置、日志）。%n此操作不可撤销。

[Tasks]
Name: "autostart"; Description: "开机自动启动 MiniClip"; GroupDescription: "附加选项："; Flags: unchecked
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加选项："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; 这个标记文件的作用是**授权导入**，不再是「数据放哪」的开关。
;
; 无论有没有它，只要程序目录可写，数据都放在程序目录下的 data\ 里。它的存在只是允许
; 本次启动把 %LOCALAPPDATA%\MiniClip 里可能残留的旧版数据一次性复制过来，以照顾从旧
; 版本升级的用户——他们的历史还在那里。
;
; 这个区分是必要的：从 bin\ 直接跑的构建也会有自己的 data\，但绝不能顺手把正式安装的
; 历史拿走，所以只有安装包写入的标记才授权导入。
;
; 导入是复制而非移动，且只在 data\history.json 不存在时执行，因此不会覆盖已有数据。
Source: "portable-marker.flag"; DestName: "MiniClip.portable"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\MiniClip"; Filename: "{app}\MiniClip.exe"; WorkingDir: "{app}"
Name: "{group}\卸载 MiniClip"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MiniClip"; Filename: "{app}\MiniClip.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MiniClip"; ValueData: """{app}\MiniClip.exe"""; Tasks: autostart

[Run]
Filename: "{app}\MiniClip.exe"; Description: "立即运行 MiniClip"; Flags: postinstall nowait skipifsilent

[UninstallDelete]
; 程序目录下的数据目录，以及标记文件本身。
Type: filesandordirs; Name: "{app}\data"
Type: files; Name: "{app}\MiniClip.portable"
; 程序目录不可写时会回退到用户数据目录（装进 Program Files 的典型情况），
; 那份数据也要清掉，否则“删除所有痕迹”不成立。
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

{ 结束正在运行的 MiniClip。

  这里刻意不使用 taskkill /IM MiniClip.exe：那是按映像名匹配，会把用户从别处
  （比如另一份绿色版、或另一个安装目录）正在运行的实例一起杀掉。改为按可执行文件
  的完整路径精确匹配，只结束装在本目录下的那个实例。

  另外，Inno 自带的 CloseApplications 依赖窗口消息，对托盘程序不可靠，所以这里
  自己兜底；否则卸载时 MiniClip.exe 被占用，程序目录删不干净。

  注意：本段注释里刻意不写花括号常量名。Pascal 注释以花括号界定，正文中一旦出现
  花括号就会被当成注释结束，导致莫名其妙的语法错误。 }
function StopMiniClip(): Boolean;
var
  ResultCode: Integer;
  ExePath: String;
  Cmd: String;
begin
  Result := True;
  ExePath := ExpandConstant('{app}\MiniClip.exe');

  { WMIC 用单引号包路径；路径里本来不该有单引号，有的话直接放弃，不冒险拼接。 }
  if Pos('''', ExePath) > 0 then
    Exit;

  Cmd := '/c wmic process where "ExecutablePath=''' + ExePath + '''" call terminate >nul 2>&1';
  if not Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := False;

  { 给进程一点时间释放文件句柄，否则紧接着的删除仍会失败。 }
  Sleep(600);
end;

function InitializeSetup(): Boolean;
begin
  { 这里不能调用 StopMiniClip：安装目录常量要等到用户选好目录之后才初始化，
    在此之前展开它会抛 "attempted to expand the app constant before it was
    initialized" 并直接终止安装。真正结束旧实例放在 CurStepChanged(ssInstall)。 }
  Result := True;
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
  if CurStep = ssInstall then
  begin
    { 文件复制之前结束旧实例，避免占用。此时安装目录已确定，常量可用。 }
    StopMiniClip();
  end;

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
var
  DataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    { 卸载开始前先停掉程序，否则程序目录里的文件被占用删不掉。 }
    StopMiniClip();

    { 也清掉后来从设置页里打开的“开机启动”。 }
    RegDeleteValue(HKCU, StartupKey, 'MiniClip');
    ClearStartupApproval();
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { 卸载时 UninstallDelete 能处理程序目录下的已知路径，但用户可能把程序装到
      任意位置；这里取同一个常量再删一次，确保自定义安装路径下的 data 子目录也不留。
      正文里不要写花括号常量名，原因见 StopMiniClip 上方的说明。 }
    DataDir := ExpandConstant('{app}\data');
    if DirExists(DataDir) then
      DelTree(DataDir, True, True, True);
  end;
end;
