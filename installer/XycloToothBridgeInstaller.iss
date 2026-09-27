; Inno Setup Script for XycloTooth Automated Text File Bridge
; Produces a standalone, professional Windows Installer

#define MyAppName "XycloTooth File Bridge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "XycloTooth"
#define MyAppURL "https://github.com/xyclotooth/bridge"
#define MyAppExeName "XycloToothBridge.exe"

[Setup]
AppId={{4A984249-1319-482D-85F3-2C16313508D7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=..\installer\output
OutputBaseFilename=XycloToothBridge-Setup-v1.0.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start XycloTooth automatically when Windows logs in"; GroupDescription: "Windows Startup:"

[Files]
Source: "..\windows-bridge\dist\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\windows-bridge\dist\*.pdb"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
Name: "{userdocs}\XycloTooth\Input"; Permissions: users-full
Name: "{userdocs}\XycloTooth\Output"; Permissions: users-full

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Open Input Folder"; Filename: "{userdocs}\XycloTooth\Input"
Name: "{group}\Open Output Folder"; Filename: "{userdocs}\XycloTooth\Output"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--minimized"; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\XycloTooth\logs"
