; Package-WindowsInstaller.ps1 supplies a freshly extracted release ZIP.
#ifndef PayloadDir
  #error PayloadDir must refer to the extracted release
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#define ProductName "Ambermoon Immersive Edition"
#define ExeName "Ambermoon.net.exe"

[Setup]
AppId={{65B6D32A-BA5A-48BB-8576-724DAA5C8CAC}
AppName={#ProductName}
AppVersion={#ReleaseVersion}
AppPublisher=A5Projects
AppPublisherURL=https://github.com/A5Projects/Ambermoon.net
AppUpdatesURL=https://github.com/A5Projects/Ambermoon.net/releases
DefaultDirName={localappdata}\Programs\{#ProductName}
AppendDefaultDirName=no
DisableDirPage=no
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#ExeName}
SetupIconFile=..\Ambermoon.net\Resources\app.ico
OutputBaseFilename=Ambermoon-ImmersiveEdition-{#ReleaseVersion}-Windows-x64-Setup
VersionInfoVersion={#ReleaseVersion}.0
VersionInfoProductName={#ProductName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
english.StartMenuShortcut=Create a &Start menu shortcut
german.StartMenuShortcut=Verknüpfung im &Startmenü erstellen

[Tasks]
Name: "startmenu"; Description: "{cm:StartMenuShortcut}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; The uninstaller owns only package files. The game creates settings and saves at runtime.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.cfg,Saves*,Screenshots"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#ProductName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: startmenu

[InstallDelete]
; If the player turns off a previously installed shortcut, remove just that link.
Type: files; Name: "{userprograms}\{#ProductName}.lnk"; Tasks: not startmenu; Check: HadStartMenuShortcut

[Code]
function HadStartMenuShortcut: Boolean;
begin
  Result := GetPreviousData('StartMenuShortcut', '') = 'yes';
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if WizardIsTaskSelected('startmenu') then
    SetPreviousData(PreviousDataKey, 'StartMenuShortcut', 'yes')
  else
    SetPreviousData(PreviousDataKey, 'StartMenuShortcut', 'no');
end;
