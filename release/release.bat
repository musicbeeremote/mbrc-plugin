@echo Off
CD ..

CALL build.bat

echo Preparing to Copy files to release directory

COPY .\build\bin\Release\mb_remote.dll .\release\
COPY .\build\bin\firewall-utility\Release\firewall-utility.exe .\release\
COPY .\LICENSE .\release\

CD release

ECHO "Creating installer for %TAG_VERSION%"
makensis MusicBeeRemote.nsi

SETLOCAL
IF NOT "%APPVEYOR_REPO_TAG_NAME%" == "" SET TAG_VERSION=%APPVEYOR_REPO_TAG_NAME%
IF NOT "%PLUGIN_VERSION%" == "" SET TAG_VERSION=%PLUGIN_VERSION%
IF "%TAG_VERSION%" == "" (
    ECHO PLUGIN_VERSION is not set
    EXIT 1
)

ECHO "Preparing zip for %TAG_VERSION%"
7z a musicbee_remote_%TAG_VERSION%.zip mb_remote.dll firewall-utility.exe LICENSE Readme.txt
ENDLOCAL

DEL mb_remote.dll
DEL firewall-utility.exe
DEL LICENSE

MOVE *.exe .\dist
MOVE *.zip .\dist
ECHO Done