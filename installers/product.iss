#ifndef ProductName
  #error ProductName is required
#endif
[Setup]
AppId={#ProductAppId}
AppName={#ProductName}
AppVersion={#ProductVersion}
DefaultDirName={localappdata}\Programs\{#ProductName}
DefaultGroupName={#ProductName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir={#OutputDirectory}
OutputBaseFilename={#ProductName}-{#ProductVersion}-setup
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=no
RestartApplications=no
AllowNoIcons=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#ProductName}.exe,0
#if ProductName == "KotorinToys"
SetupIconFile=..\src\KotorinToys\assets\KotorinToys.ico
#endif
#if ProductName == "OfficeAssist"
SetupIconFile=..\tools\OfficeAssist\src\OfficeAssist\assets\OfficeAssist.ico
#endif
#if ProductName == "EchoType"
SetupIconFile=..\tools\EchoType\src\EchoType.App\Assets\EchoType.ico
#endif
WizardStyle=modern
[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
[Files]
Source: "{#Payload}\maintenance\*"; DestDir: "{tmp}\maintenance"; Flags: dontcopy recursesubdirs
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\{#ProductName}.exe"; IconFilename: "{app}\{#ProductName}.exe"; IconIndex: 0
#if ProductName == "EchoType"
[InstallDelete]
; v0.1.0の自己完結版から更新した際、共有ランタイムより先に旧ホストを読み込まないようにする。
Type: files; Name: "{app}\hostfxr.dll"
Type: files; Name: "{app}\Captions\hostfxr.dll"
Type: files; Name: "{app}\WindowsSpeech\hostfxr.dll"
Type: files; Name: "{app}\maintenance\hostfxr.dll"
#endif
[Code]
var
  MaintenancePrepared, RemovalCompleted: Boolean;
#if ProductName == "EchoType"
const
  DotNetDownload = 'https://dotnet.microsoft.com/ja-jp/download/dotnet/10.0';

function HasNet10Framework(const Root, FrameworkName, RequiredFile: String): Boolean;
var Folder: String; Entry: TFindRec;
begin
  Result := False;
  Folder := AddBackslash(Root) + 'shared\' + FrameworkName + '\';
  if not FindFirst(Folder + '10.0.*', Entry) then Exit;
  try
    repeat
      if (StrToIntDef(Copy(Entry.Name, 6, Length(Entry.Name)), -1) >= 0) and
         FileExists(Folder + Entry.Name + '\' + RequiredFile) then
      begin
        Result := True;
        Exit;
      end;
    until not FindNext(Entry);
  finally
    FindClose(Entry);
  end;
end;

function HasEchoTypeRuntime: Boolean;
var Root: String;
begin
  Root := GetEnv('DOTNET_ROOT_X64');
  if Root = '' then Root := GetEnv('DOTNET_ROOT');
  if Root = '' then
    if not RegQueryStringValue(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64', 'InstallLocation', Root) then
      Root := ExpandConstant('{commonpf64}\dotnet');
  Result := HasNet10Framework(Root, 'Microsoft.WindowsDesktop.App', 'PresentationFramework.dll') and
            HasNet10Framework(Root, 'Microsoft.NETCore.App', 'coreclr.dll');
end;

function MissingEchoTypeRuntime(const Interactive: Boolean): String;
var ErrorCode: Integer;
begin
  Result := 'EchoTypeには .NET 10 Desktop Runtime（x64）が必要です。Microsoft公式ページで導入後、もう一度実行してください。' + #13#10 + DotNetDownload;
  if Interactive and (MsgBox(Result + #13#10 + '公式ページを開きますか？', mbConfirmation, MB_YESNO) = IDYES) then
    ShellExec('open', DotNetDownload, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;
#endif
function Maintain(Operation: String): Boolean;
var Code: Integer; Helper, Args: String;
begin
  Helper := ExpandConstant('{app}\maintenance\KotorinToys.Maintenance.exe');
  Args := '--product "{#ProductName}" --root "' + ExpandConstant('{app}') + '/" --operation ' + Operation;
  Result := FileExists(Helper) and Exec(Helper, Args, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code);
  if Result then Result := Code = 0;
end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
var Code: Integer; Helper, Args: String;
begin
  Result := '';
#if ProductName == "EchoType"
  if not HasEchoTypeRuntime then
  begin
    Result := MissingEchoTypeRuntime(not WizardSilent);
    Exit;
  end;
#endif
  if FileExists(ExpandConstant('{app}\{#ProductName}.exe')) then
  begin
    ExtractTemporaryFiles('{tmp}\maintenance\*');
    Helper := ExpandConstant('{tmp}\maintenance\KotorinToys.Maintenance.exe');
    Args := '--product "{#ProductName}" --root "' + ExpandConstant('{app}') + '/" --operation update';
    if not Exec(Helper, Args, ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, Code) then
      Code := -1;
    if Code <> 0 then
      Result := '正常終了または製品識別を確認できないため更新を中止しました。処理を完了し、状態を確認してください。';
  end;
end;
function InitializeUninstall: Boolean;
begin
  Result := False;
#if ProductName == "EchoType"
  if not HasEchoTypeRuntime then
  begin
    Log(MissingEchoTypeRuntime(not UninstallSilent));
    Exit;
  end;
#endif
  if not UninstallSilent then
    if MsgBox('{#ProductName}を削除します。設定・履歴は保持し、対象の管理予約だけ整理します。処理中の終了拒否・外部変更時は中止します。', mbConfirmation, MB_YESNO) <> IDYES then Exit;
  Result := Maintain('remove');
  MaintenancePrepared := Result;
  if (not Result) and (not UninstallSilent) then
    MsgBox('正常終了または管理予約の整理を確認できないため削除を中止しました。設定・履歴は削除していません。', mbError, MB_OK);
end;
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usDone then RemovalCompleted := True;
end;
procedure DeinitializeUninstall;
begin
  if MaintenancePrepared and (not RemovalCompleted) and
     FileExists(ExpandConstant('{app}\{#ProductName}.exe')) then
    if not Maintain('recover') then
      Log('取消または失敗後の予約復元を確認できません。管理設定の要対応を確認してください。');
end;
