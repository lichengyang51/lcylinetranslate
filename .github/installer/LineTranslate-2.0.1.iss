#define AppName "LINE翻译多开业绩长虹版"
#define AppVersion "2.0.1"
#define AppExeName "LINE翻译多开业绩长虹版.exe"
#define SourceDir GetEnv("LINE_APP_DIR")
#define OutputDir GetEnv("LINE_OUTPUT_DIR")

[Setup]
AppId={{DDFE07EA-51A8-4D2A-8E48-0C3B8F2F4210}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=LCY
DefaultDirName={localappdata}\Programs\LINE翻译多开业绩长虹版
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=LcyLineTranslate_Setup_v2.0.1
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
UninstallDisplayName={#AppName}
SetupLogging=no

[Languages]
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他任务："

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
