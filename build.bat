@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

if /i "%~1"=="clean" (
  if exist bin rmdir /s /q bin
  echo [build] Removed bin\
  exit /b 0
)

if /i "%~1"=="msi" (
  rem Build the program and wrap it into a Windows Installer package.
  rem Everything after "msi" is passed on to the script.
  powershell -ExecutionPolicy Bypass -NoProfile -File "installer\build-msi.ps1" %2 %3 %4 %5 %6
  exit /b %ERRORLEVEL%
)

rem Windows 10 ships with the .NET Framework compiler. No SDK, no downloads.
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo [build] Could not find the .NET Framework C# compiler.
  echo [build] Expected: %%WINDIR%%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
  exit /b 1
)

if not exist bin mkdir bin

set "ICON="
if exist icon.ico set "ICON=/win32icon:icon.ico"
if not exist icon.ico echo [build] icon.ico not found - building without a custom icon.

rem The Google Fonts that ship with the viewer are embedded as resources and
rem unpacked into %%LOCALAPPDATA%%\MD Viewer\fonts the first time they are used.
set "FONTRES="
for %%f in (assets\fonts\*.woff) do set "FONTRES=!FONTRES! /resource:"%%~f",MdViewer.fonts.%%~nxf"
for %%f in (assets\fonts\manifest.txt) do set "FONTRES=!FONTRES! /resource:"%%~f",MdViewer.fonts.manifest.txt"

if "!FONTRES!"=="" (
  echo [build] WARNING - no fonts found in assets\fonts, the viewer falls back to system faces.
)

echo [build] Compiler: %CSC%
echo [build] Sources : src\*.cs
echo.

"%CSC%" /nologo /target:winexe /platform:anycpu32bitpreferred /optimize+ /debug- /codepage:65001 ^
  /out:bin\MDViewer.exe /win32manifest:app.manifest %ICON% ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  !FONTRES! ^
  src\*.cs

if errorlevel 1 (
  echo.
  echo [build] FAILED - see the errors above.
  exit /b 1
)

if exist README.md copy /y README.md bin\README.md >nul
echo.
echo [build] OK -^> bin\MDViewer.exe

if /i "%~1"=="run" (
  start "" "bin\MDViewer.exe"
)

endlocal
