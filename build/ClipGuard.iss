; Inno Setup script for ClipGuard.
;
; Build the payload first:   ./build/publish.ps1
; Then compile this:         iscc build/ClipGuard.iss
;
; Requires Inno Setup 6 (https://jrsoftware.org/isinfo.php) — it is not
; bundled with the repo. WiX or MSIX would work equally well; Inno is chosen
; here because it needs no toolchain beyond a single compiler.

#define AppName        "ClipGuard"
#define AppVersion     "1.4.2"
#define AppPublisher   "SandSlide Studio"
#define AppUrl         "https://sandslide.studio"
#define AppExe         "ClipGuard.exe"

[Setup]
AppId={{9B2F4E1C-7A63-4D18-9F5E-2C1A8D4B6E30}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=ClipGuard-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Per-user install: ClipGuard needs no elevation. It writes only to HKCU and
; %LOCALAPPDATA%, and asking for admin rights would be a bad look for a tool
; whose pitch is that it stays out of your way.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start ClipGuard when I sign in"; GroupDescription: "Startup"

[Files]
Source: "..\dist\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "{#AppExe}"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Registry]
; Mirrors what the in-app "Start with Windows" toggle writes, so the two agree.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "ClipGuard"; ValueData: """{app}\{#AppExe}"" --minimized"; \
    Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start ClipGuard"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The tray app holds a file lock on itself; stop it before removing files.
Filename: "taskkill.exe"; Parameters: "/f /im {#AppExe}"; Flags: runhidden; RunOnceId: "StopClipGuard"

[UninstallDelete]
; Clipboard history and settings are the user's data, so removing them is
; opt-in rather than automatic — see the CurUninstallStepChanged prompt below.
Type: dirifempty; Name: "{localappdata}\ClipGuard"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{localappdata}\ClipGuard');
    if DirExists(DataDir) then
    begin
      if MsgBox('Also delete ClipGuard''s clipboard history and settings?' + #13#10 +
                'Leave them if you plan to reinstall.',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
