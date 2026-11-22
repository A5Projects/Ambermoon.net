; Package-WindowsInstaller.ps1 supplies a freshly extracted release ZIP.
#ifndef PayloadDir
  #error PayloadDir must refer to the extracted release
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifdef ExperimentalEdition
  #define ProductName "Ambermoon Experimental"
  #define ProductStem "Ambermoon-Experimental"
#else
  #define ProductName "Ambermoon Immersive Edition"
  #define ProductStem "Ambermoon-ImmersiveEdition"
#endif
#ifndef NumericVersion
  #define NumericVersion ReleaseVersion + ".0"
#endif
#define ExeName "Ambermoon.net.exe"

[Setup]
#ifdef ExperimentalEdition
AppId={{57839ABF-D909-4040-829D-142BD91CB272}
#else
AppId={{65B6D32A-BA5A-48BB-8576-724DAA5C8CAC}
#endif
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
OutputBaseFilename={#ProductStem}-{#ReleaseVersion}-Windows-x64-Setup
VersionInfoVersion={#NumericVersion}
#ifdef ExperimentalEdition
VersionInfoTextVersion={#ReleaseVersion}
VersionInfoProductTextVersion={#ReleaseVersion}
#endif
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
#ifdef ExperimentalEdition
english.SeparateExperimentalFolder=This folder contains a stable Ambermoon edition. Please choose a separate folder for Ambermoon Experimental.
german.SeparateExperimentalFolder=Dieser Ordner enthält eine stabile Ambermoon-Edition. Bitte wähle für Ambermoon Experimental einen eigenen Ordner.
#endif

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
#ifdef ExperimentalEdition
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Destination, StableDirectory, ReleaseLine: String;
  Lines: TArrayOfString;
begin
  Result := CustomMessage('SeparateExperimentalFolder');
  Destination := ExpandConstant('{app}');
  if RegQueryStringValue(HKCU64,
      'Software\Microsoft\Windows\CurrentVersion\Uninstall\{65B6D32A-BA5A-48BB-8576-724DAA5C8CAC}_is1',
      'InstallLocation', StableDirectory) then
    if CompareText(RemoveBackslashUnlessRoot(ExpandFileName(StableDirectory)),
                   RemoveBackslashUnlessRoot(ExpandFileName(Destination))) = 0 then
      Exit;

  if LoadStringsFromFile(AddBackslash(Destination) + 'START-HERE.txt', Lines) then
    if GetArrayLength(Lines) >= 2 then
    begin
      ReleaseLine := Lowercase(Trim(Lines[1]));
      if (Pos('release: immersive-v', ReleaseLine) = 1) or
         (Pos('release: expanded-v', ReleaseLine) = 1) then
        Exit;
    end;
  Result := '';
end;
#endif

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
