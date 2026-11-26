#ifndef Payload
  #error Supply the staged payload path with /DPayload
#endif

[Setup]
AppId={{44E783BF-858E-4BA7-9DA3-46A829E6C588}
AppName=RTS
AppVersion=0.1.0
VersionInfoVersion=0.1.0.0
DefaultDirName={localappdata}\Programs\RTS
DefaultGroupName=RTS
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=RTS-0.1.0-Windows-x64-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayIcon={app}\RTS.exe

[Languages]
Name: "ukrainian"; MessagesFile: "compiler:Languages\Ukrainian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{userprograms}\RTS\RTS"; Filename: "{app}\RTS.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\RTS"; Filename: "{app}\RTS.exe"; WorkingDir: "{app}"; Tasks: desktopicon
