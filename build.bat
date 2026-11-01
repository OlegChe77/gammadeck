@echo off
rem Build GammaDeck.exe with the .NET Framework 4.x compiler built into Windows
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe for .NET Framework 4.x not found
  exit /b 1
)
if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /out:bin\GammaDeck.exe ^
  /win32manifest:src\app.manifest ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  src\*.cs
if errorlevel 1 exit /b 1
echo OK: bin\GammaDeck.exe
