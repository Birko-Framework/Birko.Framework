<#
.SYNOPSIS
    Sweeps the whole Birko family for vulnerable NuGet packages, including transitive ones.

.DESCRIPTION
    NuGet's audit is ALREADY ON by default in the .NET SDK (NuGetAudit=true, NuGetAuditMode=all,
    NuGetAuditLevel=low — verified 2026-08-17), so every ordinary `dotnet build` already prints
    NU1901-NU1904 for an affected project. This script does not enable anything. It exists because
    of the two gaps a build-time warning cannot close:

      1. A build only audits what you build. An advisory published against a project nobody has
         touched for a month is printed by nobody.
      2. The warning is printed and scrolls past. Only `verify-conventions` check 1 promotes it to
         an error, and only for the diff of the task in hand.

    So this is a PERIODIC check, not a build setting. Run it on a schedule, before a release, and
    after any dependency bump.

    Scope matters and is the reason this sweeps consumers too: TASK-230 found that a test-only sweep
    reported SQLitePCLRaw 2.1.10 and implied "anything newer is fine", while Birko.Sandbox — on a
    NEWER Microsoft.Data.Sqlite — was still affected at 2.1.11. A sweep scoped to one tree produced
    a fix that looked complete and was not.

.PARAMETER Root
    The Birko checkout root that holds the Framework / Framework.Tests / Consumers buckets.
    Defaults to two levels above this script (…\Birko\Framework\Birko.Framework -> …\Birko).

.PARAMETER FailOnFinding
    Exit 1 when anything is found. Use this if you wire the script into a scheduled job.

.EXAMPLE
    .\audit-dependencies.ps1
    .\audit-dependencies.ps1 -FailOnFinding
#>
[CmdletBinding()]
param(
    [string] $Root,
    [switch] $FailOnFinding
)

$ErrorActionPreference = 'Stop'

if (-not $Root) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if (-not (Test-Path $Root)) { throw "Root not found: $Root" }

$buckets = @('Framework.Tests', 'Consumers') |
    ForEach-Object { Join-Path $Root $_ } |
    Where-Object { Test-Path $_ }

if (-not $buckets) { throw "No Framework.Tests or Consumers bucket under $Root" }

# Shared projects (.shproj/.projitems) cannot restore on their own — they are audited through the
# projects that import them, which is why only real .csproj files are swept.
$projects = foreach ($b in $buckets) {
    Get-ChildItem -Path $b -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
}

Write-Host "Auditing $($projects.Count) projects under $Root" -ForegroundColor Cyan

$findings = [System.Collections.Generic.List[object]]::new()
$i = 0
foreach ($p in $projects) {
    $i++
    Write-Progress -Activity 'dotnet list package --vulnerable' -Status $p.Name `
                   -PercentComplete ([int](100 * $i / [Math]::Max($projects.Count, 1)))
    $out = & dotnet list $p.FullName package --vulnerable --include-transitive 2>&1 | Out-String
    foreach ($line in ($out -split "`r?`n")) {
        # rows look like:  > PackageName   [requested]  resolved  Severity  url
        if ($line -match '^\s+>\s+(\S+)\s+(.*?)\s+(Low|Moderate|High|Critical)\s+(\S+)\s*$') {
            $findings.Add([pscustomobject]@{
                Project  = $p.Name
                Package  = $Matches[1]
                Version  = ($Matches[2].Trim() -split '\s+')[-1]
                Severity = $Matches[3]
                Advisory = $Matches[4]
            })
        }
    }
}
Write-Progress -Activity 'dotnet list package --vulnerable' -Completed

if (-not $findings.Count) {
    Write-Host "No vulnerable packages found across $($projects.Count) projects." -ForegroundColor Green
    exit 0
}

# Group by package: one advisory usually spans many projects, and the count is the remediation cost.
$rank = @{ 'Critical' = 0; 'High' = 1; 'Moderate' = 2; 'Low' = 3 }
Write-Host ''
Write-Host "$($findings.Count) finding(s) across $(($findings.Project | Select-Object -Unique).Count) project(s):" -ForegroundColor Yellow
$findings |
    Group-Object Package, Version, Severity |
    Sort-Object { $rank[$_.Group[0].Severity] }, { -$_.Count } |
    ForEach-Object {
        $f = $_.Group[0]
        $colour = if ($f.Severity -in 'Critical', 'High') { 'Red' } else { 'Yellow' }
        Write-Host ''
        Write-Host ("  {0} {1}  [{2}]  x{3} project(s)" -f $f.Package, $f.Version, $f.Severity, $_.Count) -ForegroundColor $colour
        Write-Host ("    {0}" -f $f.Advisory) -ForegroundColor DarkGray
        $_.Group.Project | Sort-Object -Unique | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
    }

Write-Host ''
Write-Host 'Remediation: prefer bumping the TOP-LEVEL package that pulls the vulnerable one' -ForegroundColor Cyan
Write-Host '(`dotnet nuget why <project> <package>` shows the chain); pin only when no bump clears it.' -ForegroundColor Cyan
Write-Host 'Check the RESOLVED transitive, not the top-level number — a newer top-level can still be affected.' -ForegroundColor Cyan

if ($FailOnFinding) { exit 1 }
exit 0
