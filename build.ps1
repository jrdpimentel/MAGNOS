# Builds the release packages:  powershell -ExecutionPolicy Bypass -File build.ps1 -Version 0.1.0
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
Remove-Item release, stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory release, stage\server-pkg, stage\client-pkg | Out-Null

Write-Host "Building server $Version..." -ForegroundColor Cyan
dotnet publish server\MAGNOS.Server.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o stage\server-pkg\server
if ($LASTEXITCODE -ne 0) { throw "Server build failed." }
Write-Host "Building client $Version..." -ForegroundColor Cyan
dotnet publish client\MAGNOS.Client.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o stage\client-pkg\client
if ($LASTEXITCODE -ne 0) { throw "Client build failed." }

Copy-Item installer\install-server.ps1, installer\uninstall.ps1 stage\server-pkg\
Copy-Item installer\install-client.ps1, installer\uninstall.ps1 stage\client-pkg\
Copy-Item README.md, LICENSE stage\server-pkg\ -ErrorAction SilentlyContinue
Copy-Item README.md, LICENSE stage\client-pkg\ -ErrorAction SilentlyContinue

Compress-Archive -Path stage\server-pkg\* -DestinationPath "release\MAGNOS-Server-v$Version-win-x64.zip" -Force
Compress-Archive -Path stage\client-pkg\* -DestinationPath "release\MAGNOS-Client-v$Version-win-x64.zip" -Force
Remove-Item stage -Recurse -Force
Write-Host "`nDone. Packages are in the 'release' folder:" -ForegroundColor Green
Get-ChildItem release | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}

