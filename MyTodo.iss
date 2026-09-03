; MyTodo 安装包脚本（Inno Setup 6）
; 编译前先完成 dotnet publish（散件产物在 publish\ 目录），
; 安装后的目录形态与常规 Windows 软件一致（exe + 运行时散件）。

#define MyAppName "MyTodo"
#ifndef MyAppVersion
  ; 版本号自动读取自发布产物；Win32 文件版本恒为四段，
  ; csproj 约定三段式版本（如 1.1.0），这里裁掉补位的 ".0"。
  #define VerStr GetVersionNumbersString("publish\MyTodo.exe")
  #define MyAppVersion Copy(VerStr, 1, Len(VerStr) - 2)
#endif
#define MyAppExeName "MyTodo.exe"

[Setup]
AppId={{8C1F5E2A-3D44-4B6B-9A07-51F0E2C9D831}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
; 按用户级安装，无需管理员权限；需要时可选择管理员方式
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={localappdata}\Programs\MyTodo
DisableProgramGroupPage=yes
OutputDir=dist
OutputBaseFilename=MyTodo-{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 覆盖升级时检测并关闭正在运行的实例
CloseApplications=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=MyTodo\Assets\app.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#if FileExists(AddBackslash(CompilerPath) + "Languages\ChineseSimplified.isl")
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
    Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
    Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall skipifsilent

[Code]
// 安装完成后，如果用户此前开启过开机启动（注册表里已有旧路径），
// 把启动项更新到新安装位置，避免指向已删除的旧目录。
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if RegValueExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MyTodo') then
      RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
        'MyTodo', ExpandConstant('{app}\MyTodo.exe'));
end;
