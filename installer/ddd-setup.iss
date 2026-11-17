; DDD installer (Inno Setup 6). Built by build-release.ps1:
;   ISCC /DAppVersion=0.1.0 /DSourceDir=..\publish\win-x64 /O..\release installer\ddd-setup.iss

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\win-x64"
#endif
#define AppUrl "https://github.com/Taoism/DDD"

[Setup]
; keep this AppId: it lets new versions upgrade older DDD installs in place
AppId={{7C7B0258-A698-42F1-A9C8-FA140586D531}
AppName=DDD - Drivers, Drivers, Drivers
AppVersion={#AppVersion}
AppVerName=DDD {#AppVersion}
AppPublisher=Taoism
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\DDD
DefaultGroupName=DDD
DisableProgramGroupPage=yes
AllowNoIcons=yes
; installs for the current user without an administrator prompt; the dialog offers all users
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=..\src\DDD\Assets\ddd.ico
UninstallDisplayIcon={app}\DDD.exe
UninstallDisplayName=DDD - Drivers, Drivers, Drivers
LicenseFile=..\LICENSE
OutputBaseFilename=DDD-{#AppVersion}-setup-win-x64
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "addtopath"; Description: "Add ddd-cli to PATH (use it from any command prompt)"; GroupDescription: "Command line:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; an installed copy must not run in portable mode
Type: files; Name: "{app}\portable.txt"

[Icons]
Name: "{group}\DDD"; Filename: "{app}\DDD.exe"
Name: "{group}\{cm:UninstallProgram,DDD}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DDD"; Filename: "{app}\DDD.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DDD.exe"; Description: "{cm:LaunchProgram,DDD}"; Flags: nowait postinstall skipifsilent

[Code]
// PATH entry for ddd-cli: the user's PATH for a per-user install, the system PATH for all users
function EnvironmentKey(var Root: Integer): String;
begin
  if IsAdminInstallMode then
  begin
    Root := HKEY_LOCAL_MACHINE;
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  end
  else
  begin
    Root := HKEY_CURRENT_USER;
    Result := 'Environment';
  end;
end;

function PathContains(Paths, Dir: String): Boolean;
begin
  Result := Pos(';' + Uppercase(Dir) + ';', ';' + Uppercase(Paths) + ';') > 0;
end;

procedure AddToPath(Dir: String);
var
  Root: Integer;
  Key, Paths: String;
begin
  Key := EnvironmentKey(Root);
  if not RegQueryStringValue(Root, Key, 'Path', Paths) then
    Paths := '';
  if PathContains(Paths, Dir) then
    exit;
  if (Paths <> '') and (Copy(Paths, Length(Paths), 1) <> ';') then
    Paths := Paths + ';';
  RegWriteExpandStringValue(Root, Key, 'Path', Paths + Dir);
end;

procedure RemoveFromPath(Dir: String);
var
  Root, P: Integer;
  Key, Paths: String;
begin
  Key := EnvironmentKey(Root);
  if not RegQueryStringValue(Root, Key, 'Path', Paths) then
    exit;
  Paths := ';' + Paths + ';';
  P := Pos(';' + Uppercase(Dir) + ';', Uppercase(Paths));
  if P = 0 then
    exit;
  Delete(Paths, P, Length(Dir) + 1);
  Paths := Copy(Paths, 2, Length(Paths) - 2);
  RegWriteExpandStringValue(Root, Key, 'Path', Paths);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
