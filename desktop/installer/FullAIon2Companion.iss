; Instalador de FullAIon2Companion (Daeva Companion para AION 2).
; Compilar: ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\release\1.0.0\app FullAIon2Companion.iss
; Se instala por usuario (sin permisos de administrador) en %LOCALAPPDATA%\Programs. Los datos del usuario
; (%LOCALAPPDATA%\FullAIon2Companion) no se tocan al actualizar ni al desinstalar.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\release\" + AppVersion + "\app"
#endif
#ifndef OutputDir
  #define OutputDir "..\release\" + AppVersion
#endif

[Setup]
AppId={{6F2B7C1E-4A8D-4E3B-9B51-2C7A9D0E5F41}
AppName=FullAIon2Companion
AppVersion={#AppVersion}
AppVerName=FullAIon2Companion {#AppVersion}
AppPublisher=Daeva Companion
AppPublisherURL=https://governanceai.me/
AppSupportURL=https://governanceai.me/support/
AppUpdatesURL=https://governanceai.me/
VersionInfoVersion={#AppVersion}
VersionInfoProductName=FullAIon2Companion
VersionInfoDescription=Instalador de FullAIon2Companion para AION 2
DefaultDirName={localappdata}\Programs\FullAIon2Companion
DefaultGroupName=FullAIon2Companion
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=FullAIon2Companion-{#AppVersion}-setup
SetupIconFile=..\FullAIon2Companion\assets\app.ico
UninstallDisplayIcon={app}\FullAIon2Companion.exe
UninstallDisplayName=FullAIon2Companion
LicenseFile=..\THIRD-PARTY-NOTICES.txt
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
#ifdef SignTool
SignTool={#SignTool}
SignedUninstaller=yes
#endif

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; Una actualización reemplaza los archivos del programa por completo (no deja restos de versiones anteriores).
Type: filesandordirs; Name: "{app}\assets"
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\ui"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\FullAIon2Companion"; Filename: "{app}\FullAIon2Companion.exe"
Name: "{group}\{cm:UninstallProgram,FullAIon2Companion}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\FullAIon2Companion"; Filename: "{app}\FullAIon2Companion.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\FullAIon2Companion.exe"; Description: "{cm:LaunchProgram,FullAIon2Companion}"; Flags: nowait postinstall skipifsilent
; Actualización desde la app ("Actualizar ahora", instalación silenciosa): vuelve a abrir la versión nueva al terminar.
Filename: "{app}\FullAIon2Companion.exe"; Flags: nowait; Check: IsSilentUpdate

[Code]
function IsSilentUpdate: Boolean;
begin
  Result := WizardSilent;
end;

// La captura de AION 2 necesita Npcap. Su licencia no permite incluirlo en este instalador, así que se
// comprueba y, si falta, se ofrece abrir la página oficial de descarga.
function NpcapInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\Npcap\wpcap.dll'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssPostInstall) and not NpcapInstalled and not WizardSilent then
    if MsgBox('Daeva Companion necesita Npcap para leer el combate de AION 2 y no está instalado en este equipo.' + #13#10#13#10 +
              '¿Abrir ahora la página oficial de descarga (npcap.com)?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://npcap.com/#download', '', '', SW_SHOWNORMAL, ewNoWait, Code);
end;
