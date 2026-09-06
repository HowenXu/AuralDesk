; AuralDesk 安装脚本（Inno Setup 6）
#define MyAppName "AuralDesk"
#define MyAppVersion "0.1.37"
#define MyAppExeName "AuralDesk.exe"
#define MyLauncherExeName "AuralDesk.Launcher.exe"

[Setup]
AppId={{7F4C9B2E-1A3D-4E5F-9B6C-2D8E4F0A1B3C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=AuralDesk
DefaultDirName={autopf}\AuralDesk
DefaultGroupName=AuralDesk
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=app.ico
OutputDir=installer
OutputBaseFilename=AuralDesk-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："

[Dirs]
; sidecar 运行时要向 data 目录写日志/凭证，装在 Program Files 下必须给普通用户写权限
Name: "{app}\qqapi\app\web\data"; Permissions: users-modify

[Files]
Source: "release\AuralDesk\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "qq_inject.js"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AuralDesk.Launcher\bin\Release\net48\{#MyLauncherExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\AuralDesk.Launcher\bin\Release\net48\{#MyLauncherExeName}.config"; DestDir: "{app}"; Flags: ignoreversion
; QQ 音乐组件（Python sidecar）。用随包便携 Python（qqapi\python），venv 绑定开发机路径不可移植故排除；
; 同时排除开发机自身的登录凭证/日志/缓存，避免打包泄露
Source: "qqapi\*"; DestDir: "{app}\qqapi"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "__pycache__,*.pyc,*.pyo,.git,venv\*,data\credentials.sqlite3*,data\logs,data\device.json,data\.gitkeep,*.log"
; data 目录权限由 [Dirs] 设置；.gitkeep 占位保证目录随包创建
Source: "qqapi\app\web\data\.gitkeep"; DestDir: "{app}\qqapi\app\web\data"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyLauncherExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyLauncherExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyLauncherExeName}"; Description: "运行 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function VersionMajorAtLeast(DirName: string; MinMajor: Integer): Boolean;
var
  dotPos: Integer;
  major: Integer;
begin
  Result := False;
  dotPos := Pos('.', DirName);
  if dotPos > 0 then
  begin
    major := StrToIntDef(Copy(DirName, 1, dotPos - 1), 0);
    Result := major >= MinMajor;
  end;
end;

function IsDotNet8DesktopInstalled(): Boolean;
var
  dir: string;
  findRec: TFindRec;
  i: Integer;
  roots: array of string;
begin
  Result := False;
  SetArrayLength(roots, 2);
  // 安装进程是 32 位，WOW64 会把 ProgramFiles 重定向到 (x86)，
  // 必须用 ProgramW6432 才能拿到真正的 64 位 Program Files，避免漏检 x64 运行时。
  roots[0] := GetEnv('ProgramW6432');
  roots[1] := GetEnv('ProgramFiles(x86)');
  for i := 0 to GetArrayLength(roots) - 1 do
  begin
    if Length(roots[i]) = 0 then
      Continue;
    dir := AddBackslash(roots[i]) + 'dotnet\shared\Microsoft.WindowsDesktop.App';
    if DirExists(dir) and FindFirst(AddBackslash(dir) + '*', findRec) then
    begin
      try
        repeat
          if (findRec.Name <> '.') and (findRec.Name <> '..') and
             ((findRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
             VersionMajorAtLeast(findRec.Name, 8) then
          begin
            Result := True;
            Exit;
          end;
        until not FindNext(findRec);
      finally
        FindClose(findRec);
      end;
    end;
  end;
end;

procedure OpenDotNetDownload(Sender: TObject);
var
  errorCode: Integer;
begin
  ShellExec('open', 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe', '',
            '', SW_SHOWNORMAL, ewNoWait, errorCode);
end;

function ShowMissingRuntimeDialog(): Integer;
var
  F: TForm;
  Lbl: TNewStaticText;
  BtnDownload: TNewButton;
  BtnRetry: TNewButton;
  BtnExit: TNewButton;
begin
  Result := mrCancel;
  F := TForm.Create(nil);
  try
    F.Caption := 'AuralDesk';
    F.ClientWidth := 500;
    F.ClientHeight := 235;
    F.Position := poScreenCenter;
    F.FormStyle := fsStayOnTop;
    F.BorderStyle := bsDialog;

    Lbl := TNewStaticText.Create(F);
    Lbl.Parent := F;
    Lbl.Caption := '未检测到 .NET 桌面运行时（8.0 及以上）。' + #13#10 +
                   'AuralDesk 需要它才能运行，请前往巨硬官网下载并安装。' + #13#10 +
                   '安装完成后点击"重试"，或重新运行本安装程序。';
    Lbl.Left := 20;
    Lbl.Top := 18;
    Lbl.Width := 460;
    Lbl.Height := 100; // 高 DPI 下给足高度，避免文字裁切
    Lbl.WordWrap := True;

    BtnDownload := TNewButton.Create(F);
    BtnDownload.Parent := F;
    BtnDownload.Caption := '前往巨硬下载';
    BtnDownload.Left := 20;
    BtnDownload.Top := 152;
    BtnDownload.Width := 150;
    BtnDownload.Height := 32;
    BtnDownload.OnClick := @OpenDotNetDownload;

    BtnExit := TNewButton.Create(F);
    BtnExit.Parent := F;
    BtnExit.Caption := '退出安装';
    BtnExit.Left := 240;
    BtnExit.Top := 152;
    BtnExit.Width := 110;
    BtnExit.Height := 32;
    BtnExit.ModalResult := mrCancel;

    BtnRetry := TNewButton.Create(F);
    BtnRetry.Parent := F;
    BtnRetry.Caption := '重试';
    BtnRetry.Left := 380;
    BtnRetry.Top := 152;
    BtnRetry.Width := 100;
    BtnRetry.Height := 32;
    BtnRetry.ModalResult := mrRetry;

    Result := F.ShowModal;
  finally
    F.Free;
  end;
end;

function InitializeSetup(): Boolean;
var
  Res: Integer;
begin
  Result := True;
  if not IsDotNet8DesktopInstalled() then
  begin
    repeat
      Res := ShowMissingRuntimeDialog();
      if Res = mrRetry then
      begin
        if IsDotNet8DesktopInstalled() then
        begin
          Result := True;
          Exit;
        end;
      end
      else
      begin
        Result := False;
        Exit;
      end;
    until False;
  end;
end;
