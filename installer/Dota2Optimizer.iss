; Inno Setup 6 script for Dota 2 Optimizer.
; Build:  iscc /DAppVersion=1.0.0 installer\Dota2Optimizer.iss
; Expects: artifacts\publish\Dota2Optimizer.exe (dotnet publish, see README)
;          installer\tools\PresentMon-*.exe + PresentMon-LICENSE.txt (optional, downloaded by CI)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Dota 2 Optimizer"
#define AppExe "Dota2Optimizer.exe"

[Setup]
AppId={{6B0E3B8A-2E5B-4C7E-9A61-7D2F0C9D2A57}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Dota2Optimizer
DefaultDirName={autopf}\Dota2Optimizer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Per-user install by default: the app itself does not need admin rights to be installed.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=Dota2Optimizer-Setup
SetupIconFile=..\src\Dota2Optimizer.App\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=installer-info.txt

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
; PresentMon (Intel, MIT license) for FPS in the match monitor. Optional: the exe also carries an embedded copy.
Source: "tools\PresentMon-*.exe"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist
Source: "tools\PresentMon-LICENSE.txt"; DestDir: "{app}\tools"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; User data (%LOCALAPPDATA%\Dota2Optimizer: backups, logs, history) is intentionally kept on uninstall,
; so changes made by the app can still be rolled back after reinstalling.
