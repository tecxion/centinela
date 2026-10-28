; Inno Setup 6 script: Centinela-<version>-windows-x64.exe, a per-user installer of the self-contained publish.
; Compiled by tools/build-installer.ps1, which passes AppVersion, SourceDir (the publish folder) and OutputDir.
; ASCII only: the Spanish texts come from Spanish.isl, so the file needs no encoding care.

#ifndef AppVersion
  #error Compila con tools\build-installer.ps1 (pasa AppVersion, SourceDir y OutputDir).
#endif

[Setup]
AppId={{698899FF-46CA-4C8B-94A8-9FEF1B8CA9B9}
AppName=Centinela
AppVersion={#AppVersion}
AppPublisher=Tecxart
AppPublisherURL=https://www.tecxart.es
AppSupportURL=https://github.com/tecxion/centinela/issues
AppUpdatesURL=https://github.com/tecxion/centinela/releases
; The folder the 1.3 was published into, so installing over it updates it in place.
DefaultDirName={localappdata}\Programs\Centinela
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=Centinela-{#AppVersion}-windows-x64
SetupIconFile=..\..\src\Centinela.Desktop\Assets\centinela.ico
UninstallDisplayIcon={app}\Centinela.exe
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart"; Description: "Arrancar con Windows"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Centinela"; Filename: "{app}\Centinela.exe"
Name: "{autodesktop}\Centinela"; Filename: "{app}\Centinela.exe"; Tasks: desktopicon

[Registry]
; The same value WindowsAutoStart writes, so the app's own "Arrancar con Windows" shows it ticked.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Centinela"; ValueData: """{app}\Centinela.exe"" --tray"; Tasks: autostart

[Run]
Filename: "{app}\Centinela.exe"; Description: "{cm:LaunchProgram,Centinela}"; Flags: nowait postinstall skipifsilent

[Code]
// The app can turn autostart on by itself too, so the value goes on uninstall whoever wrote it.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Centinela');
end;
