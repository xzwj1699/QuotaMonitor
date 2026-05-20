@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe at %CSC%
  exit /b 1
)

"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ ^
  /win32icon:assets\quota-monitor.ico ^
  /out:QuotaMonitor.exe ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Web.Extensions.dll ^
  QuotaMonitorNetFx.cs

if errorlevel 1 exit /b %errorlevel%
echo Built QuotaMonitor.exe
