# Run as Administrator. Removes the server and/or client from this PC.
#   .\uninstall.ps1                 remove services, tasks, firewall rules, restrictions (keeps data and accounts)
#   .\uninstall.ps1 -RemoveData     also delete C:\MAGNOS (including the database!) and the Customer account
param([switch]$RemoveData, [string]$CustomerName = "Customer")
$ErrorActionPreference = "Continue"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Please run this script as Administrator." }

function Clear-Restrictions([string]$hive) {
  cmd /c "reg unload HKU\OCRestr >nul 2>&1"
  cmd /c "reg load HKU\OCRestr `"$hive`" >nul 2>&1"
  if ($LASTEXITCODE -ne 0) { return }
  $sys = "HKU\OCRestr\Software\Microsoft\Windows\CurrentVersion\Policies\System"
  $exp = "HKU\OCRestr\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
  foreach ($v in 'DisableTaskMgr','DisableRegistryTools') { cmd /c "reg delete $sys /v $v /f >nul 2>&1" }
  foreach ($v in 'NoRun','NoControlPanel','DisallowRun') { cmd /c "reg delete $exp /v $v /f >nul 2>&1" }
  cmd /c "reg delete $exp\DisallowRun /f >nul 2>&1"
  cmd /c "reg delete HKU\OCRestr\Software\Policies\Microsoft\Windows\System /v DisableCMD /f >nul 2>&1"
  [gc]::Collect(); Start-Sleep 2
  cmd /c "reg unload HKU\OCRestr >nul 2>&1"
}

Unregister-ScheduledTask -TaskName "MAGNOS Client" -Confirm:$false -ErrorAction SilentlyContinue
Remove-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name MAGNOSClient -ErrorAction SilentlyContinue
Get-Process MAGNOS.Client -ErrorAction SilentlyContinue | Stop-Process -Force
if (Get-Service MAGNOSServer -ErrorAction SilentlyContinue) {
  Stop-Service MAGNOSServer -Force -ErrorAction SilentlyContinue
  Get-Process MAGNOS.Server -ErrorAction SilentlyContinue | Stop-Process -Force
  sc.exe delete MAGNOSServer | Out-Null
}
Remove-NetFirewallRule -DisplayName "MAGNOS Server" -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName "MAGNOS Discovery" -ErrorAction SilentlyContinue

Clear-Restrictions "C:\Users\Default\NTUSER.DAT"
$acct = Get-LocalUser -Name $CustomerName -ErrorAction SilentlyContinue
if ($acct) {
  $prof = Get-CimInstance Win32_UserProfile | Where-Object SID -eq $acct.SID.Value
  if ($prof -and -not $prof.Loaded) { Clear-Restrictions (Join-Path $prof.LocalPath "NTUSER.DAT") }
}
if ($RemoveData) {
  if (Test-Path C:\MAGNOS) { icacls C:\MAGNOS /reset /T /C | Out-Null; Remove-Item C:\MAGNOS -Recurse -Force }
  if ($acct) { Remove-LocalUser -Name $CustomerName }
  Write-Host "Data and the '$CustomerName' account removed." -ForegroundColor Yellow
}
Write-Host "Uninstall finished." -ForegroundColor Green

