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

REM Restore packages
echo Restoring NuGet packages...
"%programfiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\bin\MSBuild.exe" MBRC.sln /t:Restore /p:Configuration="%config%" /v:M

REM Build
echo Building solution...
"%programfiles%\Microsoft Visual Studio\2022\Community\MSBuild\Current\bin\MSBuild.exe" MBRC.sln /p:Configuration="%config%";Platform="Any CPU" /m /v:M /fl /flp:LogFile=msbuild.log;Verbosity=Normal /nr:false


