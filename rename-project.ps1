# Renames the whole project (files, code, installers, docs) from "OpenCafe" to your own name.
#   powershell -ExecutionPolicy Bypass -File rename-project.ps1 -NewName CafeDeck
# Use letters and numbers only. Run it ONCE, before your first commit. Back up the folder first.
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9]{2,24}$')][string]$NewName)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$skip = '\\(bin|obj|dist|release|stage|\.git)\\'
$files = Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch $skip -and $_.Extension -in '.cs','.csproj','.ps1','.md','.html','.json','.yml','.config','.bat','.gitignore','' -and $_.Name -ne 'rename-project.ps1' }
$n = 0
foreach ($f in $files) {
  $t = Get-Content $f.FullName -Raw -ErrorAction SilentlyContinue
  if (-not $t) { continue }
  $new = $t.Replace('OpenCafe', $NewName).Replace('opencafe', $NewName.ToLower()).Replace('OPENCAFE', $NewName.ToUpper())
  if ($new -ne $t) { Set-Content $f.FullName $new -Encoding UTF8; $n++ }
}
Get-ChildItem -Recurse -File | Where-Object { $_.FullName -notmatch $skip -and $_.Name -like '*OpenCafe*' -and $_.Name -ne 'rename-project.ps1' } | ForEach-Object { Rename-Item $_.FullName ($_.Name.Replace('OpenCafe', $NewName)) }
Write-Host "Renamed to '$NewName' in $n files." -ForegroundColor Green
Write-Host "Install folders are now C:\$NewName\Server and C:\$NewName\Client. Rebuild with build.ps1."
