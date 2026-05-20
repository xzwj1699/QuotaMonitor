$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "Could not find csc.exe at $csc"
}

& $csc /nologo /target:winexe /platform:x64 /optimize+ `
    /win32icon:assets\quota-monitor.ico `
    /out:QuotaMonitor.exe `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /reference:System.Web.Extensions.dll `
    QuotaMonitorNetFx.cs

Write-Host "Built QuotaMonitor.exe"
