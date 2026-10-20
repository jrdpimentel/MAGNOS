# Run as Administrator on each customer PC, from the extracted client package.
#   .\install-client.ps1 -ClientKey KEY -SetupCustomer      real customer PC (recommended)
#   .\install-client.ps1 -ClientKey KEY -TestMode           try it out: normal window, no lock-down
param(
  [Parameter(Mandatory)][string]$ClientKey,
  [string]$ServerUrl = "",        # empty = find the server automatically
  [string]$ClientName = "",       # empty = name from the IP (192.168.1.101 -> PC-01)
  [switch]$SetupCustomer,         # create the Customer account, lock folders, apply restrictions, auto-start
  [string]$CustomerName = "Customer",
  [switch]$TestMode,
  [switch]$StartNow
)
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Please run this script as Administrator." }
if (-not (Test-Path "$PSScriptRoot\client")) { throw "Run this script from the extracted client package (the 'client' folder must be next to it)." }

function Find-Server {
  $u = New-Object Net.Sockets.UdpClient; $u.EnableBroadcast = $true
  $b = [Text.Encoding]::ASCII.GetBytes("MAGNOS?")
  [void]$u.Send($b, $b.Length, "255.255.255.255", 5001)
  $u.Client.ReceiveTimeout = 3000
  try {
    $ep = New-Object Net.IPEndPoint([Net.IPAddress]::Any, 0)
    $r = [Text.Encoding]::ASCII.GetString($u.Receive([ref]$ep))
    if ($r -like "MAGNOS|*") { return "http://$($ep.Address):$($r.Split('|')[1])" }
  } catch {} finally { $u.Close() }
  return $null
}

function Set-Setting($xml, [string]$key, [string]$value) {
  $n = $xml.configuration.appSettings.add | Where-Object key -eq $key
  if ($n) { $n.value = $value }
  else { $e = $xml.CreateElement("add"); $e.SetAttribute("key", $key); $e.SetAttribute("value", $value); [void]$xml.configuration.appSettings.AppendChild($e) }
}

# Applies the customer restrictions (no Run box, cmd, PowerShell, regedit, Control Panel, Task Manager) to a user profile file.
function Set-Restrictions([string]$hive) {
  cmd /c "reg unload HKU\OCRestr >nul 2>&1"
  cmd /c "reg load HKU\OCRestr `"$hive`" >nul 2>&1"
  if ($LASTEXITCODE -ne 0) { Write-Warning "Could not load $hive (is that user signed in?). Skipped."; return }
  try {
    $sys = "HKU\OCRestr\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    $exp = "HKU\OCRestr\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
    reg add $sys /v DisableTaskMgr /t REG_DWORD /d 1 /f | Out-Null
    reg add $sys /v DisableRegistryTools /t REG_DWORD /d 1 /f | Out-Null
    reg add $exp /v NoRun /t REG_DWORD /d 1 /f | Out-Null
    reg add $exp /v NoControlPanel /t REG_DWORD /d 1 /f | Out-Null
    reg add $exp /v DisallowRun /t REG_DWORD /d 1 /f | Out-Null
    $i = 1; foreach ($n in 'powershell.exe','powershell_ise.exe','pwsh.exe','taskkill.exe','regedit.exe','wmic.exe','mmc.exe') { reg add "$exp\DisallowRun" /v $i /t REG_SZ /d $n /f | Out-Null; $i++ }
    reg add "HKU\OCRestr\Software\Policies\Microsoft\Windows\System" /v DisableCMD /t REG_DWORD /d 2 /f | Out-Null
  } finally {
    [gc]::Collect(); Start-Sleep 2
    cmd /c "reg unload HKU\OCRestr >nul 2>&1"
  }
}

if ($ServerUrl -eq "") {
  Write-Host "Looking for the server on the network..."
  $ServerUrl = Find-Server
  if (-not $ServerUrl) { throw "Server not found. Is it running, and on the same network? You can pass -ServerUrl http://SERVER-IP:5000" }
  Write-Host "Found server: $ServerUrl" -ForegroundColor Green
}

$dest = "C:\MAGNOS\Client"
Get-Process MAGNOS.Client -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep 1
New-Item -ItemType Directory -Force $dest | Out-Null
icacls $dest /reset /T /C | Out-Null            # make sure we can write before re-locking
Copy-Item "$PSScriptRoot\client\*" $dest -Recurse -Force

$cfgFile = "$dest\MAGNOS.Client.dll.config"
if (-not (Test-Path $cfgFile)) { $cfgFile = "$dest\MAGNOS.Client.exe.config" }
[xml]$x = Get-Content $cfgFile
Set-Setting $x "ServerUrl" $ServerUrl
Set-Setting $x "ClientKey" $ClientKey
Set-Setting $x "ClientName" $ClientName
Set-Setting $x "TestMode" ($(if ($TestMode) { "True" } else { "False" }))
$x.Save($cfgFile)

$taskName = "MAGNOS Client"
if ($SetupCustomer) {
  $acct = Get-LocalUser -Name $CustomerName -ErrorAction SilentlyContinue
  if (-not $acct) {
    $pw = Read-Host "Choose a password for the '$CustomerName' account (write it down)" -AsSecureString
    New-LocalUser -Name $CustomerName -Password $pw -FullName "Cafe Customer" -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
  } else { Write-Host "Account '$CustomerName' already exists - keeping it." }
  try { Add-LocalGroupMember -SID S-1-5-32-545 -Member $CustomerName } catch {}   # standard Users group

  # Customers can run the client but not change it.
  icacls $dest /inheritance:r /grant:r "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" | Out-Null

  # Restrictions: the Default profile (applies to NEW accounts) and the Customer profile if it already exists.
  Set-Restrictions "C:\Users\Default\NTUSER.DAT"
  $sid = (Get-LocalUser -Name $CustomerName).SID.Value
  $prof = Get-CimInstance Win32_UserProfile | Where-Object SID -eq $sid
  if ($prof -and -not $prof.Loaded) { Set-Restrictions (Join-Path $prof.LocalPath "NTUSER.DAT") }

  # Start at the Customer's logon, and relaunch within a minute if it is ever closed.
  Remove-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name MAGNOSClient -ErrorAction SilentlyContinue
  $a = New-ScheduledTaskAction -Execute "$dest\MAGNOS.Client.exe"
  $t1 = New-ScheduledTaskTrigger -AtLogOn -User $CustomerName
  $t2 = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1) -RepetitionDuration (New-TimeSpan -Days 3650)
  $p = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\$CustomerName" -LogonType Interactive
  $s = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
  Register-ScheduledTask -TaskName $taskName -Action $a -Trigger @($t1, $t2) -Principal $p -Settings $s -Force | Out-Null
  Write-Host "`nCustomer PC set up. Sign out, then sign in as '$CustomerName' to see the lock screen." -ForegroundColor Green
  Write-Host "Staff exit on the lock screen: Ctrl+Shift+F12, then the admin key."
} else {
  Set-ItemProperty "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run" -Name MAGNOSClient -Value "`"$dest\MAGNOS.Client.exe`""
  Write-Host "`nClient installed. It starts at every sign-in." -ForegroundColor Green
}
if ($StartNow) { Start-Process "$dest\MAGNOS.Client.exe" }
if (-not $TestMode -and -not $SetupCustomer) { Write-Host "Tip: use -TestMode to try the client without the full-screen lock." }

