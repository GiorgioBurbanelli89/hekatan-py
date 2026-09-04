; Inno Setup Script para Hekatan Python3
; Genera un instalador setup.exe
; Variante Python-only: motor Python nativo en C# + fallback a python real.

#define MyAppName "Hekatan Python3"
#define MyAppVersion "1.0.19"
#define MyAppPublisher "Jorge Burbano"
#define MyAppURL "https://github.com/GiorgioBurbanelli89/Calcpad-Suite-Py"
#define MyAppExeName "HekatanPython3.exe"
#define MyAppPublishDir "C:\Users\j-b-j\Desktop\HekatanPython3-Installer\HekatanPython3"

[Setup]
AppId={{F1E2D3C4-B5A6-4789-9C0D-1E2F3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\Hekatan Python3
DefaultGroupName=Hekatan Python3
AllowNoIcons=yes
LicenseFile=LICENSE
OutputDir=.\Installer
OutputBaseFilename=HekatanPython3-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "fileassoc_py"; Description: "Asociar archivos .py (Python) con Hekatan Python3"; GroupDescription: "Asociaciones de archivo:"

[InstallDelete]
; Limpiar Examples viejos antes de copiar — evita .py huérfanos de versiones anteriores.
Type: filesandordirs; Name: "{app}\Examples"

[Files]
; Application files — self-contained .NET 10 publish
Source: "{#MyAppPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

; Examples — scripts .py (Python) curados, bundleados en {app}\Examples.
Source: "Examples-Py\*"; DestDir: "{app}\Examples"; Flags: ignoreversion recursesubdirs skipifsourcedoesntexist

; Documentation
Source: "README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme skipifsourcedoesntexist
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Examples (bundleados)"; Filename: "{app}\Examples"
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; .py file association
Root: HKA; Subkey: "Software\Classes\.py\OpenWithProgids"; ValueType: string; ValueName: "HekatanPython3.PyFile"; ValueData: ""; Flags: uninsdeletevalue; Tasks: fileassoc_py
Root: HKA; Subkey: "Software\Classes\HekatanPython3.PyFile"; ValueType: string; ValueName: ""; ValueData: "Hekatan Python3 — Python Document"; Flags: uninsdeletekey; Tasks: fileassoc_py
Root: HKA; Subkey: "Software\Classes\HekatanPython3.PyFile\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"; Tasks: fileassoc_py
Root: HKA; Subkey: "Software\Classes\HekatanPython3.PyFile\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: fileassoc_py

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Iniciar Hekatan Python3"; Flags: nowait postinstall skipifsilent
