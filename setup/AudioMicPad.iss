#define MyAppName "AudioMicPad"
#define MyAppVersion "1.1.3"
#define MyAppPublisher "lilbuda"
#define MyAppExeName "AudioMicPad.exe"
#define VBCableUrl "https://vb-audio.com/Cable/"
#define ProjectUrl "https://github.com/lilbuda/AudioMicPad"

[Setup]
AppId={{A79A260B-221F-427E-87AE-43C496B2D50F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#ProjectUrl}
AppSupportURL={#ProjectUrl}/issues
AppUpdatesURL={#ProjectUrl}/releases
VersionInfoVersion={#MyAppVersion}.0
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
OutputDir=..\installer
OutputBaseFilename=AudioMicPad-Setup-v1.1.3
SetupIconFile=..\audiomicpad.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=VB-CABLE-NOTICE.txt
LicenseFile=..\THIRD-PARTY-NOTICES.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Install VB-CABLE (official site)"; Filename: "{#VBCableUrl}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{#VBCableUrl}"; Description: "Download VB-CABLE from VB-Audio (required)"; Flags: postinstall shellexec skipifsilent; Check: not IsVBCableInstalled
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: IsVBCableInstalled

[Code]
function IsVBCableInstalled: Boolean;
var
  ResultCode: Integer;
  PowerShellPath: String;
  Parameters: String;
begin
  PowerShellPath := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Parameters := '-NoProfile -NonInteractive -Command "' +
    '$device = Get-PnpDevice -Class AudioEndpoint -ErrorAction SilentlyContinue | ' +
    'Where-Object { $_.FriendlyName -like ''*CABLE Input*'' }; ' +
    'if ($null -ne $device) { exit 0 }; exit 1"';

  Result := Exec(PowerShellPath, Parameters, '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;
