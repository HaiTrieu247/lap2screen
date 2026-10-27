; Inno Setup 6 script for MacExtend.
; CI: ISCC /DAppVersion=1.2.3 /DSourceDir=<staged app folder> /O<output dir> installer\MacExtend.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\build\MacExtend"
#endif

#define FirewallRule "MacExtend (uxplay)"

[Setup]
AppId={{6F1C2B9E-3D4A-4E57-9C61-2A8B7D5E4F10}
AppName=MacExtend
AppVersion={#AppVersion}
AppVerName=MacExtend {#AppVersion}
AppPublisher=MacExtend
AppComments=Turns this Windows laptop into a second display for a Mac
DefaultDirName={autopf}\MacExtend
DefaultGroupName=MacExtend
DisableProgramGroupPage=yes
OutputBaseFilename=MacExtend-{#AppVersion}-setup
SetupIconFile=..\src\MacExtend.App\Assets\app.ico
UninstallDisplayIcon={app}\MacExtend.exe
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
; "x64" is understood by every Inno Setup 6 release (x64compatible needs 6.3+, and the runner image may ship older)
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
PrivilegesRequired=admin
WizardStyle=modern
; MacExtend holds Local\MacExtend.SingleInstance while running; close it (and uxplay) before upgrading
AppMutex=MacExtend.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\MacExtend"; Filename: "{app}\MacExtend.exe"
Name: "{autodesktop}\MacExtend"; Filename: "{app}\MacExtend.exe"; Tasks: desktopicon

[Run]
; Same rule the app's "Cho phép" button creates (Services/NetworkSetup.cs): drop block rules Windows may
; have added for uxplay.exe, then allow it on private and domain networks.
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""$p = '{app}\uxplay\bin\uxplay.exe'; Get-NetFirewallApplicationFilter -Program $p -ErrorAction SilentlyContinue | Get-NetFirewallRule -ErrorAction SilentlyContinue | Where-Object Action -eq 'Block' | Remove-NetFirewallRule; Get-NetFirewallRule -DisplayName '{#FirewallRule}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule; New-NetFirewallRule -DisplayName '{#FirewallRule}' -Direction Inbound -Program $p -Action Allow -Profile Private,Domain | Out-Null"""; \
  Flags: runhidden waituntilterminated; StatusMsg: "Allowing MacExtend through Windows Firewall..."
Filename: "{app}\MacExtend.exe"; Description: "{cm:LaunchProgram,MacExtend}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Get-NetFirewallRule -DisplayName '{#FirewallRule}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule"""; \
  Flags: runhidden waituntilterminated; RunOnceId: "RemoveFirewallRule"

[Registry]
; the app writes its own start-with-Windows entry per user; remove it on uninstall for the installing user
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "MacExtend"; Flags: uninsdeletevalue dontcreatekey
; DPI compatibility flag the app sets for uxplay.exe (Services/SystemServices.cs, DpiCompat)
Root: HKCU; Subkey: "Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"; ValueType: none; ValueName: "{app}\uxplay\bin\uxplay.exe"; Flags: uninsdeletevalue dontcreatekey
