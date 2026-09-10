; ============================================================================
;  PMK Unlock Tool — Windows EXE Installer (Inno Setup 6)
;  Build:  python tools\make_installer.py            (version ကို csproj ကနေ ဖတ်တယ်)
;          iscc tools\installer\PMK-Unlock-Tool.iss /DMyAppVersion=4.0.17
;
;  မှတ်ချက်: per-user install (LocalAppData\Programs) ပဲ — tool က ကိုယ့် folder ထဲမှာ
;  license.dat / users.dat / session.dat / Loaders / updates တွေ ရေးတာမို့ folder က
;  writable ဖြစ်ရမယ်။ Program Files ထဲ ထည့်ရင် အဲဒါတွေ မသိမ်းနိုင်ဘဲ auto-login / activation
;  အလုပ်မလုပ်တော့ဘူး (per-user ဖြစ်လို့ in-app auto update လည်း admin မလိုဘူး)။
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#define MyAppName "PMK Unlock Tool"
#define MyAppPublisher "PMK Mobile Service"
#define MyAppExeName "WinFormsApp1.exe"
#define SourceDir "..\..\Publish\win-x64"

[Setup]
AppId={{8F3B6C42-5D19-4F7E-9A21-0C6B7E4D3A55}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableReadyPage=yes
AllowNoIcons=yes
; per-user ONLY — Program Files ထဲ ထည့်ရင် tool က license/session/Loaders ဖိုင်တွေ ရေးလို့မရဘူး
PrivilegesRequired=lowest
OutputDir=..\..\Releases
OutputBaseFilename=PMK-Unlock-Tool-Setup-v{#MyAppVersion}
SetupIconFile=..\..\WinFormsApp1\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} v{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
SetupLogging=yes
AppComments=Android flashing & repair tool (Qualcomm EDL, MediaTek, Samsung, Spreadtrum, ADB, Fastboot)

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; ---- Application payload (Publish folder တစ်ခုလုံး) ----
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; \
    Excludes: "users.dat,users.dat.*,session.dat,license.dat,theme.txt,flash_options.txt,crash.log,google_client.json,updates,device_database,Loaders,Backups,*.log"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Comment: "PMK Unlock Tool v{#MyAppVersion}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; App က ဖန်တီးတဲ့ log/temp ဖိုင်တွေပဲ ဖျက် — user data (users.dat/license/Loaders) ကို [Code] မှာ မေးပြီးမှ
Type: files; Name: "{app}\crash.log"
Type: filesandordirs; Name: "{app}\updates"

[Code]
var
  DeleteUserData: Boolean;
  PythonMissing: Boolean;

// ---- App ပိတ်ထားဖို့ သေချာအောင် ----
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  // tasklist နဲ့ စစ် (Restart Manager က CloseApplications=yes နဲ့ ပိတ်ပေးတယ်၊ ဒါက သတိပေးဖို့)
  Result := Exec('cmd.exe', '/C tasklist /FI "IMAGENAME eq {#MyAppExeName}" /NH | find /I "{#MyAppExeName}" > nul', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

// ---- Python ရှိ/မရှိ (tool က edl.py / mtk.py အတွက် လိုတယ်) ----
function HasPython(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('cmd.exe', '/C python --version > nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if not Result then
    Result := Exec('cmd.exe', '/C py -3 --version > nul 2>&1', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    PythonMissing := not HasPython();
    if PythonMissing then
    begin
      if SuppressibleMsgBox('Python မတွေ့ပါ (Qualcomm EDL / MediaTek flash လုပ်ဖို့ လိုအပ်ပါတယ်).' + #13#10 + #13#10 +
                'python.org ကနေ Python 3 install လုပ်ပြီး အောက်ပါ command ကို run ပေးပါ:' + #13#10 +
                '    pip install pyserial pyusb' + #13#10 + #13#10 +
                'အခု python.org download page ကို ဖွင့်ပြမလား?',
                mbConfirmation, MB_YESNO, IDNO) = IDYES then
        ShellExec('open', 'https://www.python.org/downloads/windows/', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
      end;
  end;
end;

// ---- Uninstall မှာ user data ဖျက်/မဖျက် မေး ----
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if SuppressibleMsgBox('User data တွေကိုပါ ဖျက်မလား?' + #13#10 + #13#10 +
            '• တွေ့ပါက ဖျက်မယ့်အရာ: users.dat (account စာရင်း), session.dat (login session),' + #13#10 +
            '  license.dat (activation), theme.txt, flash_options.txt, device_database, Loaders (download ထားတဲ့ loader ဖိုင်တွေ)' + #13#10 + #13#10 +
            'No ဆိုရင် ဒီဖိုင်တွေ ကျန်နေမယ် (ပြန် install လုပ်ရင် account/license ပြန်မထည့်ရတော့ဘူး).',
            mbConfirmation, MB_YESNO, IDNO) = IDYES then
    DeleteUserData := True
  else
    DeleteUserData := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  AppDir: String;
begin
  if (CurUninstallStep = usUninstall) and DeleteUserData then
  begin
    AppDir := ExpandConstant('{app}');
    DeleteFile(AppDir + '\users.dat');
    DeleteFile(AppDir + '\session.dat');
    DeleteFile(AppDir + '\license.dat');
    DeleteFile(AppDir + '\theme.txt');
    DeleteFile(AppDir + '\flash_options.txt');
    DeleteFile(AppDir + '\google_client.json');
    DelTree(AppDir + '\device_database', True, True, True);
    DelTree(AppDir + '\Loaders', True, True, True);
    DelTree(AppDir + '\Backups', True, True, True);
  end;
end;
