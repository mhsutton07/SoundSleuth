@echo off
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Cannot find the .NET Framework 4 C# compiler at %CSC%
  exit /b 1
)
if not exist bin mkdir bin
if not exist "%TEMP%\ssp" mkdir "%TEMP%\ssp"
"%CSC%" /nologo /target:exe /optimize+ /warnaserror+ /r:System.Management.dll /out:"%TEMP%\ssp\MuteProbe.exe" MuteProbe.cs ..\src\CoreAudioInterop.cs ..\src\ProcessInfo.cs ..\src\SessionMonitor.cs
if errorlevel 1 exit /b 1
copy /y "%TEMP%\ssp\MuteProbe.exe" bin\MuteProbe.exe >nul
echo Built tests\bin\MuteProbe.exe
exit /b 0
