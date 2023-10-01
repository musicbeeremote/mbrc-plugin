@echo Off
set config=%1
if "%config%" == "" (
   set config=Release
)

set version=
if not "%PackageVersion%" == "" (
   set version=-Version %PackageVersion%
)

REM Remove Previous output
rmdir %cd%\build /s /q

REM Package restore
tools\nuget.exe restore -OutputDirectory %cd%\packages -NonInteractive

REM Build
"%programfiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\bin\MSBuild.exe" MBRC.sln /p:Configuration="%config%";Platform="Any CPU" /m /v:M /fl /flp:LogFile=msbuild.log;Verbosity=Normal /nr:false


