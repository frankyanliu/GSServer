; Script generated by the ASCOM Driver Installer Script Generator 6.2.0.0
; Generated by Robert Morgan on 5/20/2018 (UTC)
#define MyAppVersion "1.0.1.61"
#define ManualName "GSS Manual v10161.pdf"
#define VersionNumber "v10161"
#define InstallerBaseName "ASCOMGSServer10161Setup"
#define MyAppName "GSServer"
#define MyAppExeName "GS.Server.exe"

[Setup]
AppVerName=ASCOM GS Server {#MyAppVersion}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
OutputBaseFilename={#InstallerBaseName}
AppID={{0ff78bd6-6149-4536-9252-3da68b94f7c2}
AppName=GS Server
AppPublisher=Robert Morgan <robert.morgan.e@gmail.com>
AppPublisherURL=mailto:robert.morgan.e@gmail.com
AppSupportURL=https://ascomtalk.groups.io/g/Developer/topics
AppUpdatesURL=http://ascom-standards.org/
MinVersion=0,6.1
DefaultDirName="{cf}\ASCOM\Telescope\GSServer"
DefaultGroupName="GS Server"
DisableDirPage=yes
DisableProgramGroupPage=yes
OutputDir="."
Compression=lzma
SetupIconFile="C:\Users\Rob\source\repos\GSSolution\Resources\Installer\greenswamp2.ico"
SetupLogging=yes
SolidCompression=yes
; Put there by Platform if Driver Installer Support selected
WizardImageFile="C:\Users\Rob\source\repos\GSSolution\Resources\Installer\WizardImage1.bmp"
LicenseFile="C:\Users\Rob\source\repos\GSSolution\Resources\Installer\License.txt"
; {cf}\ASCOM\Uninstall\Telescope folder created by Platform, always
UninstallFilesDir="{cf}\ASCOM\Uninstall\Telescope\GSServer"
;"C:\Program Files (x86)\Windows Kits\10\Tools\bin\i386\signtool.exe" sign /f "C:\Users\Rob\source\repos\GSSolution\Resources\Installer\GreenSwamp.pfx" /p rem /d "GreenSwamp Installer"  $f
SignTool=Signtool 

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Dirs]
Name: "{cf}\ASCOM\Uninstall\Telescope\GSServer\"
Name: "{cf}\ASCOM\Uninstall\Telescope\GSServer\SkyScripts\"
Name: "{cf}\ASCOM\Uninstall\Telescope\GSServer\Notes\NotesTemplates\"
Name: "{cf}\ASCOM\Uninstall\Telescope\GSServer\Models"
Name: "{cf}\ASCOM\Uninstall\Telescope\GSServer\LanguageFiles"
; TODO: Add subfolders below {app} as needed (e.g. Name: "{app}\MyFolder")

[Files]
Source: "C:\Users\Rob\source\repos\GSSolution\Builds\Release\*.*"; DestDir: "{app}"
Source: "C:\Users\Rob\source\repos\GSSolution\Builds\Release\SkyScripts\*.*"; DestDir: "{app}\SkyScripts";
Source: "C:\Users\Rob\source\repos\GSSolution\Builds\Release\Notes\NotesTemplates\*.*"; DestDir: "{app}\NotesTemplates";
Source: "C:\Users\Rob\source\repos\GSSolution\Builds\Release\Models\*.*"; DestDir: "{app}\Models";
Source: "C:\Users\Rob\source\repos\GSSolution\Builds\Release\LanguageFiles\*.*"; DestDir: "{app}\LanguageFiles";
; Require a read-me to appear after installation, maybe driver's Help doc
Source: "C:\Users\Rob\source\repos\GSSolution\Resources\Manuals\GSS Manual.pdf"; DestDir: "{app}"; DestName:"{#ManualName}"; Flags: isreadme
; TODO: Add other files needed by your driver here (add subfolders above)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; \
    GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Icons]
Name: "{group}\GS Server {#VersionNumber}"; Filename: "{app}\GS.Server.exe"
Name: "{group}\GS Chart Viewer {#VersionNumber}"; Filename: "{app}\GS.ChartViewer.exe"
Name: "{group}\GS Utilities {#VersionNumber}"; Filename: "{app}\GS.Utilities.exe"
Name: "{group}\GS Manual {#VersionNumber}"; Filename: "{app}\{#ManualName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; Only if driver is .NET
[Run]

; Only for .NET local-server drivers
Filename: "{app}\GS.Server.exe"; Parameters: "/register"

; Only if driver is .NET
[UninstallRun]
; This helps to give a clean uninstall

; Only for .NET local-server drivers, use /unprofile to remove ascom profile 
Filename: "{app}\GS.Server.exe"; Parameters: "/unregister /unprofile"

[CODE]
//
// Before the installer UI appears, verify that the (prerequisite)
// ASCOM Platform 6.4 or greater is installed, including both Helper
// components. Utility is required for all types (COM and .NET)!
//
function InitializeSetup(): Boolean;
var
   U: Variant;
   H: Variant;
begin
   Result := FALSE;  // Assume failure
   // check that the DriverHelper and Utilities objects exist, report errors if they don't
   try
      H := CreateOleObject('DriverHelper.Util');
   except
      MsgBox('The ASCOM DriverHelper object has failed to load, this indicates a serious problem with the ASCOM installation', mbInformation, MB_OK);
   end;
   try
      U := CreateOleObject('ASCOM.Utilities.Util');
   except
      MsgBox('The ASCOM Utilities object has failed to load, this indicates that the ASCOM Platform has not been installed correctly', mbInformation, MB_OK);
   end;
   try
      if (U.IsMinimumRequiredVersion(6,4)) then	// this will work in all locales
         Result := TRUE;
   except
   end;
   if(not Result) then
      MsgBox('The ASCOM Platform 6.4 or greater is required for this driver.', mbInformation, MB_OK);
end;

// Code to enable the installer to uninstall previous versions of itself when a new version is installed
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  UninstallExe: String;
  UninstallRegistry: String;
begin
  if (CurStep = ssInstall) then // Install step has started
	begin
      // Create the correct registry location name, which is based on the AppId
      UninstallRegistry := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}' + '_is1');
      // Check whether an extry exists
      if RegQueryStringValue(HKLM, UninstallRegistry, 'UninstallString', UninstallExe) then
        begin // Entry exists and previous version is installed so run its uninstaller quietly after informing the user
          MsgBox('Setup will now remove the previous version.', mbInformation, MB_OK);
          Exec(RemoveQuotes(UninstallExe), ' /SILENT', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode);
          sleep(1000);    //Give enough time for the install screen to be repainted before continuing
        end
  end;
end;