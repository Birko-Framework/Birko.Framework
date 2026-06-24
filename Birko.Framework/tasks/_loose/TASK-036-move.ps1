<#
  TASK-036 — workspace reorg move script.
  Generated for manual execution (the agent sandbox blocks mutations under C:\Source).

  USAGE:
    Run from a shell whose working directory is NOT inside C:\Source (e.g. cd C:\):
      pwsh C:\Source\Birko.Framework\tasks\_loose\TASK-036-move.ps1            # dry-run (default, shows actions)
      pwsh C:\Source\Birko.Framework\tasks\_loose\TASK-036-move.ps1 -Execute   # actually move/delete

  WHAT IT DOES (per TASK-036, finstat bucket dropped 2026-06-18):
    - Birko.*  (non-Tests)   -> C:\Source\Birko\Framework\
    - Birko.*.Tests          -> C:\Source\Birko\Framework.Tests\
    - listed consumers       -> C:\Source\Birko\Consumers\
    - scratch                -> C:\Source\aicode\   (test -> renamed Latent first)
    - antigravity, Wedding, bin, obj  -> DELETED (confirmed scratch/garbage)
    - finstat product code + WhMan + EventSourcing -> LEFT FLAT AT ROOT (no action)

  Birko.Framework is moved LAST. If you run this from inside it, the script refuses
  (Windows can't move the folder a running process is anchored in).
#>
param([switch]$Execute)

$ErrorActionPreference = 'Stop'
$root = 'C:\Source'
$mode = if ($Execute) { 'EXECUTE' } else { 'DRY-RUN' }
Write-Host "TASK-036 move — $mode`n" -ForegroundColor Cyan

# Refuse to run from inside C:\Source (we move folders out from under the cwd)
if ((Get-Location).Path -like "$root*") {
  Write-Host "ABORT: run this from outside C:\Source (e.g. 'cd C:\' first)." -ForegroundColor Red
  exit 1
}

$consumers = @('Symbio','Symbio.Core','Symbio.Monitor','gameshow-app','WorkoutTracker','Presenter','BardStudio','DraCode')
$finstat   = @('finstat','finstat-other','api-documentation','ClientApi.CSharp','ClientApi.PHP','SuperFaktura','SuperFakturaAPI.NET','DataSetExtractor')
$rootkeep  = @('WhMan','EventSourcing')
$aicode    = @('Snake','symbio_test','InternalDevMeetup','CheesyBot','DraCode-Projects','leon')
$delete    = @('antigravity','Wedding','bin','obj')

function Bucket($n){
  if ($n -in @('Birko','aicode')) { return $null }
  if ($delete -contains $n) { return 'DELETE' }
  if ($n -eq 'test') { return 'aicode' }   # renamed to Latent on the way
  if ($n -like 'Birko.*' -and $n -like '*.Tests') { return 'Framework.Tests' }
  if ($n -like 'Birko.*') { return 'Framework' }
  if ($consumers -contains $n -or $n -like 'Affiliate*' -or $n -like 'FisData.Stock*') { return 'Consumers' }
  if ($finstat -contains $n) { return $null }   # leave flat at root
  if ($rootkeep -contains $n) { return $null }  # leave flat at root
  if ($aicode -contains $n) { return 'aicode' }
  return 'UNCLASSIFIED'
}

# Ensure buckets exist
foreach ($b in @("$root\Birko\Framework","$root\Birko\Framework.Tests","$root\Birko\Consumers","$root\aicode")) {
  if ($Execute) { New-Item -ItemType Directory -Force -Path $b | Out-Null }
}

$dirs = Get-ChildItem -Path $root -Directory -Force | Sort-Object Name
$unclassified = @(); $deferSelf = $null; $moved = 0; $deleted = 0

foreach ($d in $dirs) {
  $n = $d.Name
  $b = Bucket $n
  if ($null -eq $b) { continue }                       # left at root / is a bucket
  if ($b -eq 'UNCLASSIFIED') { $unclassified += $n; continue }

  if ($b -eq 'DELETE') {
    Write-Host ("DELETE  {0}" -f $n) -ForegroundColor Yellow
    if ($Execute) { Remove-Item -Path $d.FullName -Recurse -Force -Confirm:$false }
    $deleted++; continue
  }

  # Birko.Framework moved last (self-move guard)
  if ($n -eq 'Birko.Framework') { $deferSelf = $d; continue }

  $srcName = $n
  # test -> Latent rename before moving into aicode
  if ($n -eq 'test') {
    Write-Host "RENAME  test -> Latent" -ForegroundColor DarkCyan
    if ($Execute) { Rename-Item -Path $d.FullName -NewName 'Latent'; $srcName = 'Latent' }
    else { $srcName = 'Latent' }
  }

  $destDir = if ($b -eq 'aicode') { "$root\aicode" } else { "$root\Birko\$b" }
  Write-Host ("MOVE    {0,-40} -> {1}\" -f $srcName, $destDir)
  if ($Execute) { Move-Item -Path (Join-Path $root $srcName) -Destination $destDir }
  $moved++
}

# Self-move last
if ($deferSelf) {
  Write-Host ("MOVE    {0,-40} -> {1}\Birko\Framework\  (self / done last)" -f 'Birko.Framework', $root) -ForegroundColor Magenta
  if ($Execute) { Move-Item -Path $deferSelf.FullName -Destination "$root\Birko\Framework" }
  $moved++
}

Write-Host ""
Write-Host ("Summary: {0} move(s), {1} delete(s)." -f $moved, $deleted) -ForegroundColor Green
if ($unclassified.Count) { Write-Host ("UNCLASSIFIED (skipped): {0}" -f ($unclassified -join ', ')) -ForegroundColor Red }
if (-not $Execute) { Write-Host "`nDry-run only. Re-run with -Execute to apply." -ForegroundColor Cyan }
