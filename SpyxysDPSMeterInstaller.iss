#define MyAppName "Spyxy's DPS"
#define MyAppVersion "1.0.3"
#define MyAppPublisher "khadesh"
#define MyAppURL "https://github.com/khadesh/SpyxysDPSMeter"
#define MyAppExeName "SpyxysDPSMeter.exe"

[Setup]
; Never change AppId after releasing the first installer.
AppId=khadesh.SpyxysDPSMeter

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Per-user location keeps settings.json writable.
DefaultDirName={localappdata}\Programs\SpyxysDPSMeter
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

PrivilegesRequired=lowest

OutputDir=InstallerOutput
OutputBaseFilename=SpyxysDPSMeter-Setup-{#MyAppVersion}-win-x64

SetupIconFile=SpyxysDPSMeter\Assets\icon-spyxy-dps.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

Compression=lzma2
SolidCompression=yes
WizardStyle=modern

ArchitecturesAllowed=x64compatible

CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; \
    Description: "Create a desktop shortcut"; \
    GroupDescription: "Additional shortcuts:"; \
    Flags: unchecked

[Files]
Source: "SpyxysDPSMeter\publish\win-x64\*"; \
    DestDir: "{app}"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    WorkingDir: "{app}"

Name: "{autodesktop}\{#MyAppName}"; \
    Filename: "{app}\{#MyAppExeName}"; \
    WorkingDir: "{app}"; \
    Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
    Description: "Launch {#MyAppName}"; \
    WorkingDir: "{app}"; \
    Flags: nowait postinstall skipifsilent





