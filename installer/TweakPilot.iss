; Installer (Abschnitt 12.1): Programmordner, Startmenue, kein Autostart, kein Dienst, keine geplante Aufgabe.
; Optionaler zweiter Weg neben der Installation pro Benutzer aus der App (V2 Abschnitt 7.3). Uebernimmt den Paketordner
; von package.ps1 (TweakPilot.exe oben, Helfer und Kommandozeile in "app"). Auf dem Entwicklungsrechner nicht gebaut:
; Inno Setup ist dort nicht installiert. Aufruf: ISCC.exe /DAppVersion=... /DSourceDir=<Paketordner> installer\TweakPilot.iss

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\..\TweakPilot"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6F2C1B9E-4A7D-4C51-9E2B-8D3A5F0C7E14}
AppName=TweakPilot
AppVersion={#AppVersion}
AppVerName=TweakPilot {#AppVersion}
; Herausgeber und Impressum stehen noch nicht fest (offene Punkte 2 und 6).
AppPublisher=TweakPilot
DefaultDirName={autopf}\TweakPilot
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=TweakPilot-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\assets\icon\tweakpilot.ico
UninstallDisplayIcon={app}\TweakPilot.exe
UninstallDisplayName=TweakPilot

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: ".tweakpilot-package"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\TweakPilot"; Filename: "{app}\TweakPilot.exe"

[Run]
Filename: "{app}\TweakPilot.exe"; Parameters: "--portable"; Description: "{cm:LaunchProgram,TweakPilot}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
german.BeforeUninstall=Änderungen, die TweakPilot angewendet hat, bleiben nach der Deinstallation aktiv. Wenn du sie zurücknehmen willst, öffne vorher TweakPilot und nutze "Alles rückgängig machen" auf der Seite Wiederherstellung.%n%nTrotzdem deinstallieren?
english.BeforeUninstall=Changes applied by TweakPilot stay active after uninstalling. If you want to undo them, open TweakPilot first and use "Undo everything" on the Restore page.%n%nUninstall anyway?
german.KeepBackups=Sollen die Sicherungen unter %ProgramData%\TweakPilot erhalten bleiben?%n%nMit ihnen lassen sich die Änderungen nach einer Neuinstallation noch zurücksetzen. "Nein" löscht sie endgültig.
english.KeepBackups=Keep the backups in %ProgramData%\TweakPilot?%n%nWith them, the changes can still be reset after reinstalling. "No" deletes them permanently.

[Code]
function InitializeUninstall(): Boolean;
begin
  Result := UninstallSilent() or (MsgBox(CustomMessage('BeforeUninstall'), mbConfirmation, MB_YESNO) = IDYES);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Sicherungen nur auf ausdruecklichen Wunsch loeschen; bei stiller Deinstallation bleiben sie (umkehrbar).
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent()) then
  begin
    if MsgBox(CustomMessage('KeepBackups'), mbConfirmation, MB_YESNO) = IDNO then
      DelTree(ExpandConstant('{commonappdata}\TweakPilot'), True, True, True);
  end;
end;
