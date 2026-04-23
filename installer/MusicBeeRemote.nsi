!include WordFunc.nsh
!insertmacro VersionCompare
!include LogicLib.nsh
!include FileFunc.nsh

!define PLUGIN_NAME "MusicBee Remote"
!define PLUGIN_NAME_ALT "musicbee_remote"
; Version is set from environment variable during CI build
!define PLUGIN_VERSION "$%PLUGIN_VERSION%"
!define PLUGIN_PUBLISHER "Konstantinos Paparas (kelsos)"
!define PLUGIN_WEBSITE "https://mbrc.kelsos.net"

!define MB_DIR_REGKEY "Software\Microsoft\Windows\CurrentVersion\App Paths\MusicBee.exe"

; Request admin privileges for Program Files installation
RequestExecutionLevel admin

SetCompressor lzma
!include "MUI2.nsh"

!define MUI_ICON "mbrc.ico"
!define MUI_UNICON "mbrc.ico"

; Version information for the installer executable
VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName" "${PLUGIN_NAME}"
VIAddVersionKey "ProductVersion" "${PLUGIN_VERSION}"
VIAddVersionKey "CompanyName" "${PLUGIN_PUBLISHER}"
VIAddVersionKey "LegalCopyright" "Copyright (C) 2011-2026 ${PLUGIN_PUBLISHER}"
VIAddVersionKey "FileDescription" "${PLUGIN_NAME} Installer"
VIAddVersionKey "FileVersion" "${PLUGIN_VERSION}"

; Welcome page
!insertmacro MUI_PAGE_WELCOME
; License page
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
; Directory page
!insertmacro MUI_PAGE_DIRECTORY
; Instfiles page
!insertmacro MUI_PAGE_INSTFILES

!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_LINK "Visit MusicBee Remote website"
!define MUI_FINISHPAGE_LINK_LOCATION "${PLUGIN_WEBSITE}"

!insertmacro MUI_PAGE_FINISH

; Uninstaller pages
!insertmacro MUI_UNPAGE_INSTFILES

; Language files
!insertmacro MUI_LANGUAGE "English"

Name "${PLUGIN_NAME} ${PLUGIN_VERSION}"
OutFile "${PLUGIN_NAME_ALT}_${PLUGIN_VERSION}.exe"

InstallDir "$PROGRAMFILES\MusicBee"
InstallDirRegKey HKLM "${MB_DIR_REGKEY}" ""
ShowInstDetails show
ShowUninstDetails show

Section "MainSection" SEC01
	DetailPrint "Checking for a MusicBee installation..."

	; Verify MusicBee.exe exists
	IfFileExists "$INSTDIR\MusicBee.exe" CheckVersion
		MessageBox MB_OK|MB_ICONEXCLAMATION "MusicBee.exe not found in the selected directory.$\n$\nPlease select the correct MusicBee installation folder."
		Abort

	CheckVersion:
	GetDllVersion "$INSTDIR\MusicBee.exe" $R0 $R1

	IntOp $R2 $R0 / 0x00010000
	IntOp $R3 $R0 & 0x0000FFFF
	IntOp $R4 $R1 >> 16
	IntOp $R4 $R4 & 0x0000FFFF

	${If} $R4 < 6500
		MessageBox MB_OK|MB_ICONEXCLAMATION "An unsupported MusicBee version has been detected.$\n$\nThe minimum required version is MusicBee 3.1 (build 6500).$\nYour version: build $R4$\n$\nPlease update MusicBee from https://getmusicbee.com"
		Abort
	${EndIf}

	DetailPrint "MusicBee build $R4 detected. Installing plugin..."

	SetOverwrite on
	SetOutPath "$INSTDIR\Plugins"
	File ..\build\dist\mb_remote.dll
	File ..\build\dist\mbrc_core.dll
	File ..\build\dist\firewall-utility.exe
	SetOverwrite off
SectionEnd

Section -AdditionalIcons
	; Create Start Menu folder
	CreateDirectory "$SMPROGRAMS\${PLUGIN_NAME}"
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
	Delete "$INSTDIR\mbrc_core.dll"
	Delete "$INSTDIR\firewall-utility.exe"
	Delete "$INSTDIR\mbremoteuninstall.exe"
	RmDir /r "$APPDATA\MusicBee\mb_remote"

	; Remove Start Menu items
	Delete "$SMPROGRAMS\${PLUGIN_NAME}\Uninstall ${PLUGIN_NAME}.lnk"
	RmDir "$SMPROGRAMS\${PLUGIN_NAME}"

	SetAutoClose true
SectionEnd

Function .onInstSuccess
	MessageBox MB_YESNO '${PLUGIN_NAME} was installed successfully!$\n$\nDo you want to run MusicBee now?' IDNO end
	ExecShell open "$INSTDIR\MusicBee.exe"
	end:
FunctionEnd

Function .onVerifyInstDir
	; Check if MusicBee.exe exists in the selected directory
	IfFileExists "$INSTDIR\MusicBee.exe" ValidDir

	; Check for Windows Store version
	StrCpy $0 "$LOCALAPPDATA\Packages"
	${GetFileAttributes} "$0" "DIRECTORY" $1
	${If} $1 == 1
		; Windows Store apps exist, might be Store version
		FindFirst $2 $3 "$0\*MusicBee*"
		${If} $3 != ""
			FindClose $2
			MessageBox MB_OK|MB_ICONEXCLAMATION "MusicBee installation not found.$\n$\nIt appears you may have the Windows Store version of MusicBee.$\nThis installer requires the standard version.$\n$\nFor the Store version, please use the ZIP file instead:$\n1. Download the ZIP from the releases page$\n2. In MusicBee: Edit > Preferences > Plugins$\n3. Click 'Add Plugin' and select the ZIP file"
			Abort
		${EndIf}
		FindClose $2
	${EndIf}

	; Generic not found message with browse option
	MessageBox MB_OKCANCEL|MB_ICONEXCLAMATION "MusicBee installation not found in:$\n$INSTDIR$\n$\nPlease select the correct MusicBee installation folder,$\nor click Cancel to exit." IDOK +2
	Abort

	; Let user try again by not aborting - they can change the directory
	Return

	ValidDir:
FunctionEnd
