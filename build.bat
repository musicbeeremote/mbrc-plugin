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
rmdir %cd%\build\bin\%config% /s /q

REM Package restore
tools\nuget.exe restore mbrc-core\packages.config -OutputDirectory %cd%\packages -NonInteractive

REM Build
"%programfiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" MBRC.sln /p:Configuration="%config%";Platform="Any CPU" /m /v:M /fl /flp:LogFile=msbuild.log;Verbosity=Normal /nr:false


