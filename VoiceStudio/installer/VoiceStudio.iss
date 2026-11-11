; Installateur de Voice Studio (Inno Setup 6).
; L'exe publié contient déjà .NET : rien d'autre à installer, sauf le câble virtuel VB-CABLE.

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
Filename: "{app}\VoiceStudio.exe"; Description: "Lancer Voice Studio"; Flags: nowait postinstall skipifsilent
