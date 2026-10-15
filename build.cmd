@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Cannot find the .NET Framework 4 C# compiler at:
  echo   %CSC%
  echo This build needs 64-bit Windows with the .NET Framework 4.x installed.
  exit /b 1
)
if not exist bin mkdir bin
set "OPTS=/nologo /target:winexe /optimize+ /warnaserror+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /win32manifest:app.manifest /win32icon:assets\SoundSleuth.ico /resource:assets\SoundSleuth.ico,SoundSleuth.ico"
"%CSC%" %OPTS% /out:bin\SoundSleuth.exe src\*.cs > "%TEMP%\ss_build.log" 2>&1
set RC=%errorlevel%
type "%TEMP%\ss_build.log"
if %RC%==0 goto ok
findstr /c:"CS2021" "%TEMP%\ss_build.log" >nul
if errorlevel 1 exit /b %RC%
echo Path too long for the compiler; rebuilding in a short temp folder.
if not exist "%TEMP%\ssb" mkdir "%TEMP%\ssb"
"%CSC%" %OPTS% /out:"%TEMP%\ssb\SoundSleuth.exe" src\*.cs
if errorlevel 1 exit /b 1
copy /y "%TEMP%\ssb\SoundSleuth.exe" bin\SoundSleuth.exe >nul
:ok
echo Built bin\SoundSleuth.exe
exit /b 0
