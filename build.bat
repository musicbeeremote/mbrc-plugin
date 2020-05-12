@echo Off
set config=%1
if "%config%" == "" (
   set config=Release
)

set version=
if not "%PackageVersion%" == "" (
   set version=-Version %PackageVersion%
)

IF EXIST "%cd%\build\bin\%config%" (
    echo "Removing directory %cd%\build\bin\%config%"
    rmdir %cd%\build\bin\%config% /s /q
)

IF NOT "%APPVEYOR%" == "True" (
    echo "Restoring NuGet packages" 
    REM Package restore
    tools\nuget.exe restore mbrc-core\packages.config -OutputDirectory %cd%\packages -NonInteractive
    tools\nuget.exe restore plugin\packages.config -OutputDirectory %cd%\packages -NonInteractive    
    if "%config%" == "Debug" (
        tools\nuget.exe restore mbrc-core.Test\packages.config -OutputDirectory %cd%\packages -NonInteractive
        tools\nuget.exe restore mbrc-tester\packages.config -OutputDirectory %cd%\packages -NonInteractive   
    )
)

REM Build
"%programfiles(x86)%\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe" MBRC.sln /p:Configuration="%config%";Platform="Any CPU" /m /v:M /fl /flp:LogFile=msbuild.log;Verbosity=Normal /nr:false /warnaserror


