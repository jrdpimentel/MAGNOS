# Run as Administrator on the server PC, from the extracted server package.
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Please run this script as Administrator." }
if (-not (Test-Path "$PSScriptRoot\server")) { throw "Run this script from the extracted server package (the 'server' folder must be next to it)." }

$dest = "C:\MAGNOS\Server"
if (Get-Service MAGNOSServer -ErrorAction SilentlyContinue) {
  Stop-Service MAGNOSServer -Force -ErrorAction SilentlyContinue
  (Get-Service MAGNOSServer).WaitForStatus('Stopped','00:00:30')
  Get-Process MAGNOS.Server -ErrorAction SilentlyContinue | Stop-Process -Force
  sc.exe delete MAGNOSServer | Out-Null; Start-Sleep 2
}
New-Item -ItemType Directory -Force $dest | Out-Null

# Keep an existing database and existing keys when upgrading.
$exclude = @("magnos.db*")
if (Test-Path "$dest\appsettings.json") { $exclude += "appsettings.json" }
Copy-Item "$PSScriptRoot\server\*" $dest -Recurse -Force -Exclude $exclude

function New-Key { -join ((48..57)+(65..90)+(97..122) | Get-Random -Count 24 | ForEach-Object {[char]$_}) }
$cfgPath = "$dest\appsettings.json"
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
if ($cfg.Cafe.AdminKey -like "CHANGE-ME*")  { $cfg.Cafe.AdminKey  = New-Key }
if ($cfg.Cafe.ClientKey -like "CHANGE-ME*") { $cfg.Cafe.ClientKey = New-Key }
$cfg | ConvertTo-Json -Depth 5 | Set-Content $cfgPath

# Dashboard + clients (TCP 5000) and automatic server detection (UDP 5001)
Remove-NetFirewallRule -DisplayName "MAGNOS Server" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "MAGNOS Discovery" -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "MAGNOS Server"    -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow -Profile Any | Out-Null
New-NetFirewallRule -DisplayName "MAGNOS Discovery" -Direction Inbound -Protocol UDP -LocalPort 5001 -Action Allow -Profile Any | Out-Null
New-Service -Name MAGNOSServer -BinaryPathName "$dest\MAGNOS.Server.exe" -DisplayName "MAGNOS Server" -StartupType Automatic | Out-Null
Start-Service MAGNOSServer

$ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "169.*" } | Select-Object -First 1).IPAddress
Write-Host "`nServer is running." -ForegroundColor Green
Write-Host "Admin dashboard : http://${ip}:5000"
Write-Host "Member portal   : http://${ip}:5000/portal/"
Write-Host "Admin key       : $($cfg.Cafe.AdminKey)"
Write-Host "Client key      : $($cfg.Cafe.ClientKey)   <- needed when installing clients"
Write-Host "SAVE BOTH KEYS somewhere private. Do not share them or upload them anywhere."

