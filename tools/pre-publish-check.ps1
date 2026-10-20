# Run before publishing:  powershell -ExecutionPolicy Bypass -File tools\pre-publish-check.ps1
Set-Location (Split-Path $PSScriptRoot -Parent)
$script:ok = $true
function Pass($m) { Write-Host "  OK    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  WARN  $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:ok = $false }

Write-Host "`n1. Keys" -ForegroundColor Cyan
$cfg = Get-Content server\appsettings.json -Raw
if ($cfg -match 'CHANGE-ME-ADMIN' -and $cfg -match 'CHANGE-ME-CLIENT') { Pass "appsettings.json has placeholder keys only" } else { Fail "appsettings.json contains real keys" }

Write-Host "`n2. License" -ForegroundColor Cyan
if (-not (Test-Path LICENSE)) { Fail "LICENSE is missing" }
elseif ((Get-Content LICENSE -Raw) -match 'contributors|Your Name') { Fail "LICENSE still has a placeholder name" } else { Pass "LICENSE has an owner name" }

Write-Host "`n3. Screenshots" -ForegroundColor Cyan
$need = '01-dashboard','02-computers','03-seat-map','04-pos','05-inventory','06-chat-activity','07-lock-screen','08-session-widget','09-member-portal'
$missing = $need | Where-Object { -not (Test-Path "docs\images\$_.png") }
if ($missing) { Warn ("missing: " + ($missing -join ', ')) } else { Pass "all 9 screenshots present" }

Write-Host "`n4. Leftover old project name" -ForegroundColor Cyan
$old = 'Open' + 'Cafe'
$hits = Get-ChildItem -Recurse -File -Include *.cs,*.md,*.ps1,*.html,*.json,*.yml,*.config,*.csproj | Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|release|stage|\.git|TestResults)\\' -and $_.Name -ne 'rename-project.ps1' } | Select-String -Pattern $old -List
if ($hits) { Warn ("old name still found in: " + (($hits | ForEach-Object { $_.Filename }) -join ', ')) } else { Pass "no leftover old name" }

Write-Host "`n5. Files that must not be published" -ForegroundColor Cyan
$bad = Get-ChildItem -Recurse -File -Include *.db,*.db-wal,*.db-shm,wallpaper.bin | Where-Object { $_.FullName -notmatch '\\(bin|obj|dist|release|stage|\.git)\\' }
if ($bad) { Warn ("found (ignored by .gitignore, but double-check): " + (($bad | ForEach-Object { $_.Name }) -join ', ')) } else { Pass "no database files in the folder" }

Write-Host "`n6. Tests" -ForegroundColor Cyan
dotnet test tests\Server.Tests\Server.Tests.csproj -c Release --nologo -v q
if ($LASTEXITCODE -eq 0) { Pass "all tests passed" } else { Fail "tests failed" }

Write-Host ""
if ($script:ok) { Write-Host "Ready to publish (check any WARN lines above)." -ForegroundColor Green } else { Write-Host "Fix the FAIL lines before publishing." -ForegroundColor Red }
