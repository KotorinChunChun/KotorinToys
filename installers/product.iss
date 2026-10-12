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
UninstallDisplayIcon={app}\{#ProductName}.exe
WizardStyle=modern
[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
[Files]
Source: "{#Payload}\maintenance\*"; DestDir: "{tmp}\maintenance"; Flags: dontcopy recursesubdirs
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\{#ProductName}.exe"
[Code]
var
  MaintenancePrepared, RemovalCompleted: Boolean;
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
