!include WordFunc.nsh
!insertmacro VersionCompare
!include LogicLib.nsh

!define PLUGIN_NAME "MusicBee Remote"
!define PLUGIN_NAME_ALT "musicbee_remote"
!ifdef $%APPVEYOR_REPO_TAG_NAME%
    !define PLUGIN_VERSION $%APPVEYOR_REPO_TAG_NAME%
!else
    !define PLUGIN_VERSION $%PLUGIN_VERSION%
!endif    

!define PLUGIN_PUBLISHER "Konstantinos Paparas (kelsos)"
!define PLUGIN_WEBSITE "http://mbrc.kelsos.net"

!define MB_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\MusicBee.exe"

SetCompressor lzma
!include "MUI2.nsh"

!define MUI_ICON "mbrc.ico"

; Welcome page
!insertmacro MUI_PAGE_WELCOME
; License page
!insertmacro MUI_PAGE_LICENSE "LICENSE"
; Directory page
!insertmacro MUI_PAGE_DIRECTORY
; Instfiles page
!insertmacro MUI_PAGE_INSTFILES

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_LINK "Visit MusicBee Remote website"
!define MUI_FINISHPAGE_LINK_LOCATION "http://kelsos.net/musicbeeremote/"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION FinishPageAction

!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_INSTFILES

; Language files
!insertmacro MUI_LANGUAGE "English"

Name "${PLUGIN_NAME} ${PLUGIN_VERSION}"
OutFile "${PLUGIN_NAME_ALT}_${PLUGIN_VERSION}.exe"

InstallDir "$PROGRAMFILES\MusicBee"
InstallDirRegKey HKLM MB_DIR_REGKEY ""
ShowInstDetails show
ShowUninstDetails show

Section "MainSection" SEC01
	DetailPrint "Checking for a MusicBee installation..."
	GetDllVersion "$INSTDIR\MusicBee.exe" $R0 $R1
	;IfErrors Abort

  IntOp $R2 $R0 / 0x00010000
  IntOp $R3 $R0 & 0x0000FFFF
  IntOp $R4 $R1 >> 16
  IntOp $R4 $R4 & 0x0000FFFF

  	${If} $R4 < 6132
  		DetailPrint 'An unsupported MusicBee version has been detected.'
  		DetailPrint "The minimum required version of MusicBee is v3.0.6132."
  		Abort
  	${EndIf}
	SetOverwrite on
	SetOutPath "$INSTDIR\Plugins"
	File mb_remote.dll
	File firewall-utility.exe
	SetOverwrite off
SectionEnd

Section -AdditionalIcons
  SetOutPath "$INSTDIR\Plugins"
  CreateShortCut "$SMPROGRAMS\${PLUGIN_NAME}\Uninstall ${PLUGIN_NAME}.lnk" "$INSTDIR\Plugins\mbremoteuninstall.exe"
SectionEnd

Section -Post
  WriteUninstaller "$INSTDIR\Plugins\mbremoteuninstall.exe"
SectionEnd

Function un.onUninstSuccess
  HideWindow
  MessageBox MB_ICONINFORMATION|MB_OK "$(^Name) was successfully removed from your computer."
FunctionEnd

Function un.onInit
  MessageBox MB_ICONQUESTION|MB_YESNO|MB_DEFBUTTON2 "Are you sure you want to completely remove $(^Name)?" IDYES +2
  Abort
FunctionEnd

Section Uninstall
	Delete "$INSTDIR\mb_remote.dll"
	Delete "$INSTDIR\firewall-utility.exe"
	Delete "$INSTDIR\mbremoteuninstall.exe"
	RmDir /r $APPDATA\MusicBee\mb_remote
	Delete "$SMPROGRAMS\${PLUGIN_NAME}\Uninstall ${PLUGIN_NAME}.lnk"
	SetAutoClose true
SectionEnd

Function .onInstSuccess
	  MessageBox MB_YESNO '${PLUGIN_NAME} was installed. Do you want to run MusicBee now?' IDNO end
    ExecShell open "$INSTDIR\MusicBee.exe"
  end:
FunctionEnd

Function .onVerifyInstDir
	IfFileExists "$INSTDIR\MusicBee.exe" Good
		Abort
	Good:
FunctionEnd
