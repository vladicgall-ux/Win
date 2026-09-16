; Inno Setup script for WinFileRecovery.
; Build the app first (see README "Publish"), then compile this script
; with Inno Setup (ISCC.exe setup.iss) to produce a single installer exe.

; TODO before a real release: MyAppPublisher below is a placeholder — set it
; to the actual legal entity name before shipping (it shows in the UAC
; prompt, Programs & Features, and should match the Authenticode cert's
; subject once the exe is signed — see .github/workflows/build-installer.yml).
#define MyAppName "WinFileRecovery"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "YourCompany"
#define MyAppExeName "WinFileRecovery.exe"
; Path to the self-contained, single-file publish output.
#define PublishDir "..\src\WinFileRecovery.App\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{B6B3B7D0-8C1B-4B7B-9C3B-6C6B6B6B6B6B}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
; The published exe already carries requireAdministrator in its manifest,
; but the installer itself also needs elevation to write to Program Files
; and create the Start Menu / uninstall entries.
PrivilegesRequired=admin
SetupIconFile=
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; PublishSingleFile already bundles all managed dependencies into one exe;
; only the exe itself needs shipping.
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
