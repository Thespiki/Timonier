; Installeur de Timonier (Inno Setup 6.3 ou plus récent).
; Compilation : tools\publish.ps1 (publie l'application puis appelle ISCC),
; ou directement : ISCC.exe /DAppVersion=0.1.0 installer\Timonier.iss
;
; Installation par machine dans Program Files : ce dossier n'est modifiable que par un administrateur.
; C'est indispensable au modèle de sécurité de Timonier, dont le processus administrateur (broker) est
; le même exécutable relancé via l'UAC : installé dans un dossier modifiable par l'utilisateur, il
; pourrait être remplacé à son insu avant d'être élevé.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#define AppName "Timonier"
#define AppExe "Timonier.exe"
#define AppPublisher "Spik"
#define AppUrl "https://github.com/Thespiki/Timonier"

[Setup]
AppId={{27A5B237-49CC-470A-9A62-B2A1997F7045}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (C) 2026 {#AppPublisher} - GPL-3.0
DefaultDirName={autopf}\{#AppName}
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#OutputDir}
OutputBaseFilename=Timonier-Setup-{#AppVersion}-x64
SetupIconFile=..\src\Timonier\Assets\Timonier.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
; L'application en cours d'exécution bloque la mise à jour et la désinstallation (mutex de l'instance unique).
AppMutex=Local\Timonier.SingleInstance
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Installation de {#AppName}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Messages]
fr.ConfirmUninstall=Voulez-vous vraiment désinstaller %1 ?%n%nLes réglages de Windows modifiés avec Timonier ne sont pas rétablis automatiquement. Pour revenir en arrière, annulez-les d'abord depuis la page « Journal » de Timonier.
en.ConfirmUninstall=Are you sure you want to uninstall %1?%n%nWindows settings changed with Timonier are not restored automatically. To revert them, undo them first from Timonier's "Journal" page.

[CustomMessages]
fr.LaunchApp=Lancer Timonier
en.LaunchApp=Launch Timonier
fr.AppComment=Centre de contrôle local pour Windows
en.AppComment=Local control center for Windows
fr.RemoveData=Supprimer aussi les données de Timonier ?%n%n- vos paramètres, le cache et les journaux d'activité (dossier %1) ;%n- le journal des modifications administrateur (registre HKLM\SOFTWARE\Timonier).%n%nSans ce journal, les modifications déjà faites ne pourront plus être annulées depuis Timonier.
en.RemoveData=Also remove Timonier's data?%n%n- your settings, cache and activity logs (folder %1);%n- the administrator change journal (registry HKLM\SOFTWARE\Timonier).%n%nWithout this journal, changes already made can no longer be undone from Timonier.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppComment}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "{cm:AppComment}"; Tasks: desktopicon

[Run]
; runasoriginaluser : l'application ne doit jamais hériter des droits administrateur de l'installeur.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  { Lancement avec Windows : valeur créée par l'application elle-même (page Paramètres). }
  RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Timonier');

  if UninstallSilent then
    Exit;

  DataDir := ExpandConstant('{localappdata}\Timonier');
  if MsgBox(FmtMessage(CustomMessage('RemoveData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(DataDir, True, True, True);
    RegDeleteKeyIncludingSubkeys(HKLM64, 'SOFTWARE\Timonier');
  end;
end;
