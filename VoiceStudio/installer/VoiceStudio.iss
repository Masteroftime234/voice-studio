; Installateur de Voice Studio (Inno Setup 6).
; L'exe publié contient déjà .NET : rien d'autre à installer, sauf le câble virtuel VB-CABLE,
; que l'installateur détecte et propose de télécharger s'il manque.

#define CableUrl "https://vb-audio.com/Cable/"

[Setup]
AppId={{6E0B5C0E-7B1D-4C55-9A3B-5C1B6C2F8A10}
AppName=Voice Studio
AppVersion=0.1.0
AppPublisher=Voice Studio
DefaultDirName={autopf}\Voice Studio
DefaultGroupName=Voice Studio
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\src\VoiceStudio\Resources\app.ico
UninstallDisplayIcon={app}\VoiceStudio.exe
OutputDir=Output
OutputBaseFilename=VoiceStudio-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; Flags: unchecked

[Files]
Source: "..\publish\VoiceStudio.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Voice Studio"; Filename: "{app}\VoiceStudio.exe"
Name: "{autodesktop}\Voice Studio"; Filename: "{app}\VoiceStudio.exe"; Tasks: desktopicon

[Run]
; Proposé uniquement si le câble virtuel n'a pas été trouvé sur la machine.
Filename: "{#CableUrl}"; Description: "Télécharger VB-CABLE (nécessaire pour Discord)"; \
  Flags: shellexec nowait postinstall skipifsilent; Check: not CableInstalled
Filename: "{app}\VoiceStudio.exe"; Description: "Lancer Voice Studio"; \
  Flags: nowait postinstall skipifsilent

[Code]
{ VB-CABLE s'installe comme pilote audio : on le reconnaît à son service ou à son fichier. }
function CableInstalled: Boolean;
begin
  Result :=
    RegKeyExists(HKEY_LOCAL_MACHINE, 'SYSTEM\CurrentControlSet\Services\VBAudioVACWDM') or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win10.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable64_win7.sys')) or
    FileExists(ExpandConstant('{sys}\drivers\vbaudio_cable_win10.sys'));
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = wpReady) and (not CableInstalled) then
    MsgBox('VB-CABLE ne semble pas installé sur ce PC.' + #13#10#13#10 +
           'C''est le micro virtuel que Discord écoutera : sans lui, Voice Studio ' +
           'n''a nulle part où envoyer le son.' + #13#10#13#10 +
           'À la fin de l''installation, laisse cochée la case qui ouvre la page de ' +
           'téléchargement. Installe VB-CABLE en tant qu''administrateur, puis redémarre le PC.',
           mbInformation, MB_OK);
end;
