param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Project = Join-Path $Root "src/QuotaMonitor.App.Avalonia/QuotaMonitor.App.Avalonia.csproj"
$PublishDir = Join-Path $Root "src/QuotaMonitor.App.Avalonia/bin/Release/net10.0/$Rid/publish"
$DistDir = Join-Path $Root "dist/windows-$Rid"

dotnet publish $Project -c Release -r $Rid --self-contained true

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
Get-ChildItem -LiteralPath $PublishDir -Force | Copy-Item -Destination $DistDir -Recurse -Force

Write-Host "Published:"
Write-Host $PublishDir
Write-Host "Windows app:"
Write-Host (Join-Path $DistDir "QuotaMonitor.exe")
