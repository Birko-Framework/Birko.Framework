<#
.SYNOPSIS
    Does any consumer declare a package version BELOW what the framework declares?

.DESCRIPTION
    The third audit sibling. audit-declarations.ps1 asks whether every shared project ACCOUNTS for
    the packages it uses; audit-dependencies.ps1 asks whether any resolved package is VULNERABLE.
    This one asks the consumer-side question that completes the ownership rule in
    CLAUDE-maintenance.md section "External dependencies":

        A consumer must never declare a package version LOWER than the framework declares.
        Higher is allowed - the consumer then owns any breakage. Equal means the consumer
        should not declare it at all.

    WHY THIS NEEDS A SCRIPT AT ALL, WHICH IS THE ONLY INTERESTING THING ABOUT IT:

    That rule is exactly NuGet's NU1605 "detected package downgrade", which is an ERROR by default
    and costs nothing. But NU1605 can only compare across a PACKAGE DEPENDENCY EDGE. Birko ships
    shared projects: a .projitems is compiled INTO the consumer's assembly and has no package
    identity, so there is no edge, and the framework's declaration and the consumer's are simply two
    PackageReference items in one project file. That is NU1504 - a warning, about duplication, with
    nothing to say about which version is lower. The guard exists, is free, and is blind to precisely
    the shape the framework ships in. Measured 2026-09-19 on Symbio: Npgsql 9.* against the
    framework's 10.*, sitting in the build for weeks, reported only as a duplicate.

    Shipping the backends as real NuGet packages would delete this script. That is deferred until the
    libraries stabilise (TASK-234 section "Out of scope"), so until then the rule is enforced here.

    THREE THINGS TO KNOW BEFORE TRUSTING A ZERO:

    1. Only $(BirkoSrc)-rooted imports are followed. A consumer that reaches the framework through a
       different property, a relative path or a symlink is INVISIBLE to this script - it will be
       reported as having no framework imports, which reads exactly like compliance. The summary
       prints the per-consumer import count for that reason: a consumer you know imports Birko and
       that shows 0 is a defect in this script, not a clean result.
    2. Only FLOORS are compared. `10.*` is floor 10.0.0; what it resolves to today is not consulted,
       deliberately - the policy is about what a project DECLARES, and a resolved version moves under
       you on the next restore.
    3. Only a project that BOTH imports a .projitems AND declares the package is checked. A sibling
       project in the same repo declaring an older version reaches the framework through a
       ProjectReference, where NuGet's own NU1605 does see it and does fail the build. Out of scope
       here on purpose, not overlooked.

    Verify the check can fail before believing it: change any consumer declaration to a lower major
    and re-run - it must report exactly that project and package as BELOW.

.PARAMETER Root
    The Birko checkout root holding the Framework / Consumers buckets. Defaults to two levels above
    this script (...\Birko\Framework\Birko.Framework -> ...\Birko).

.PARAMETER FailOnFinding
    Exit 1 when anything is found. Use this when wiring the script into a gate.

.EXAMPLE
    .\audit-consumer-versions.ps1
    .\audit-consumer-versions.ps1 -FailOnFinding
#>
[CmdletBinding()]
param(
    [string] $Root,
    [switch] $FailOnFinding
)

$ErrorActionPreference = 'Stop'

if (-not $Root) { $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
if (-not (Test-Path $Root)) { throw "Root not found: $Root" }

$frameworkBucket = Join-Path $Root 'Framework'
$consumerBucket  = Join-Path $Root 'Consumers'
foreach ($b in @($frameworkBucket, $consumerBucket)) {
    if (-not (Test-Path $b)) { throw "Bucket not found: $b" }
}

function Remove-XmlComments([string] $text) { [regex]::Replace($text, '(?s)<!--.*?-->', '') }

# A declared version -> a comparable floor. Returns $null when the shape is not understood, and the
# caller reports that as UNPARSEABLE rather than assuming it is fine: this is the one place a wrong
# guess would turn a downgrade into a clean line.
function Get-VersionFloor([string] $v) {
    if ([string]::IsNullOrWhiteSpace($v)) { return $null }
    $s = $v.Trim()
    if ($s -eq '*') { return [version]'0.0.0.0' }                   # latest stable; never below anything
    if ($s -match '^[\[\(]\s*([0-9][0-9.]*)') { $s = $Matches[1] }  # [10.0,) / [9.0.3] -> lower bound
    $s = $s -replace '-.*$', ''                                     # drop any prerelease suffix
    $s = $s -replace '\*', '0'                                      # 10.* -> 10.0
    if ($s -notmatch '^[0-9]+(\.[0-9]+)*$') { return $null }
    $parts = @($s -split '\.') + @('0', '0', '0', '0')
    return [version]::new([int]$parts[0], [int]$parts[1], [int]$parts[2], [int]$parts[3])
}

# ---- 1. what the framework declares --------------------------------------------------------------
# Only the non-CPM half of each conditioned pair carries a version, and that pair is ONE declaration.
# Counting both halves makes every package look duplicated - the first thing that went wrong when
# this was measured by hand.
$frameworkDecl = @{}
Get-ChildItem -Path $frameworkBucket -Directory -Filter 'Birko.*' | ForEach-Object {
    Get-ChildItem -Path $_.FullName -Filter '*.projitems' -File -ErrorAction SilentlyContinue | ForEach-Object {
        $owner = [IO.Path]::GetFileNameWithoutExtension($_.Name)
        $txt = Remove-XmlComments ([System.IO.File]::ReadAllText($_.FullName))
        foreach ($m in [regex]::Matches($txt, '<PackageReference\s+Include="([^"]+)"([^>/]*)')) {
            $rest = $m.Groups[2].Value
            if ($rest -match "ManagePackageVersionsCentrally\)'\s*==\s*'true'") { continue }
            if ($rest -notmatch 'Version="([^"]+)"') { continue }
            $frameworkDecl[$m.Groups[1].Value] = [pscustomobject]@{
                Version = $Matches[1]
                Owner   = $owner
            }
        }
    }
}

# ---- 2. one consumer project's $(BirkoSrc) import graph, followed transitively --------------------
function Get-ImportedProjitems([string] $projectFile, [string] $bucket) {
    $result = [System.Collections.Generic.HashSet[string]]::new()
    $seen   = [System.Collections.Generic.HashSet[string]]::new()
    $queue  = [System.Collections.Generic.Queue[string]]::new()
    $queue.Enqueue($projectFile)
    while ($queue.Count) {
        $f = $queue.Dequeue()
        if (-not (Test-Path $f)) { continue }
        $full = (Resolve-Path $f).Path
        if (-not $seen.Add($full.ToLowerInvariant())) { continue }
        $txt = Remove-XmlComments ([System.IO.File]::ReadAllText($full))
        foreach ($m in [regex]::Matches($txt, '<Import\s+Project="([^"]+)"')) {
            $p = $m.Groups[1].Value
            $p = $p.Replace('$(BirkoSrc)', $bucket)
            $p = $p.Replace('$(MSBuildThisFileDirectory)', ([IO.Path]::GetDirectoryName($full) + '\'))
            if ($p.Contains('$(')) { continue }
            if ($p -notmatch '\.projitems$') { continue }
            [void]$result.Add($p)
            $queue.Enqueue($p)
        }
    }
    return $result
}

# ---- 3. compare -----------------------------------------------------------------------------------
$findings    = [System.Collections.Generic.List[object]]::new()
$unparsed    = [System.Collections.Generic.List[object]]::new()
$perConsumer = [System.Collections.Generic.List[object]]::new()

foreach ($consumer in (Get-ChildItem -Path $consumerBucket -Directory | Sort-Object Name)) {
    $cpmFile = Join-Path $consumer.FullName 'Directory.Packages.props'
    $isCpm   = Test-Path $cpmFile
    $central = @{}
    if ($isCpm) {
        $ctxt = Remove-XmlComments ([System.IO.File]::ReadAllText($cpmFile))
        foreach ($m in [regex]::Matches($ctxt, '<PackageVersion\s+Include="([^"]+)"[^>]*Version="([^"]+)"')) {
            $central[$m.Groups[1].Value] = $m.Groups[2].Value
        }
    }

    $projects = Get-ChildItem -Path $consumer.FullName -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\(bin|obj|node_modules)\\' }

    $importCount = 0
    foreach ($proj in $projects) {
        $ptxt = Remove-XmlComments ([System.IO.File]::ReadAllText($proj.FullName))
        if (-not $ptxt.Contains('$(BirkoSrc)')) { continue }
        $imported = Get-ImportedProjitems $proj.FullName $frameworkBucket
        $importCount += $imported.Count

        # which framework-owned packages does THIS project inherit?
        $inherited = @{}
        foreach ($pi in $imported) {
            $owner = [IO.Path]::GetFileNameWithoutExtension($pi)
            foreach ($k in $frameworkDecl.Keys) {
                if ($frameworkDecl[$k].Owner -eq $owner) { $inherited[$k] = $frameworkDecl[$k] }
            }
        }

        foreach ($m in [regex]::Matches($ptxt, '<PackageReference\s+(Include|Update)="([^"]+)"([^>/]*)')) {
            $verb = $m.Groups[1].Value
            $pkg  = $m.Groups[2].Value
            if (-not $inherited.ContainsKey($pkg)) { continue }

            $own = $null
            if ($m.Groups[3].Value -match 'Version="([^"]+)"') { $own = $Matches[1] }
            elseif ($isCpm -and $central.ContainsKey($pkg))    { $own = $central[$pkg] }

            $fwVer = $inherited[$pkg].Version
            $a = Get-VersionFloor $own
            $b = Get-VersionFloor $fwVer
            if ($null -eq $a -or $null -eq $b) {
                $unparsed.Add([pscustomobject]@{
                    Consumer          = $consumer.Name
                    Project           = $proj.Name
                    Package           = $pkg
                    Consumer_Version  = $own
                    Framework_Version = $fwVer
                })
                continue
            }

            # Floors alone are not the whole story. The framework floats deliberately, so that a
            # published advisory heals on the next restore; a consumer that declares an EXACT version
            # freezes it, and does so invisibly when the floor happens to match. DraCode's
            # `Microsoft.Data.Sqlite 9.0.4` and `JwtBearer 10.0.0` are both that shape, and a
            # floor-only comparison called the second one identical to `10.*`.
            $fwFloats  = $fwVer.Contains('*')
            $ownFloats = $own -and $own.Contains('*')

            $verdict = $null
            if ($a -lt $b) {
                $verdict = 'BELOW'
            }
            elseif ($fwFloats -and -not $ownFloats) {
                $verdict = 'PINNED'
            }
            elseif ($verb -ne 'Update') {
                $verdict = if ($a -eq $b) { 'EQUAL' } else { 'HIGHER-VIA-INCLUDE' }
            }
            if (-not $verdict) { continue }

            $findings.Add([pscustomobject]@{
                Consumer  = $consumer.Name
                Project   = $proj.Name
                Package   = $pkg
                Declares  = "$verb=$own"
                Framework = "$fwVer ($($inherited[$pkg].Owner))"
                Verdict   = $verdict
            })
        }
    }
    if ($importCount) {
        $perConsumer.Add([pscustomobject]@{
            Consumer = $consumer.Name
            Imports  = $importCount
            CPM      = $(if ($isCpm) { 'yes' } else { 'no' })
        })
    }
}

# ---- 4. report ------------------------------------------------------------------------------------
$ownerCount = ($frameworkDecl.Values | ForEach-Object { $_.Owner } | Sort-Object -Unique).Count
Write-Host ''
Write-Host "Framework declares $($frameworkDecl.Count) packages across $ownerCount shared projects." -ForegroundColor Cyan
Write-Host "Consumers importing them: $($perConsumer.Count)" -ForegroundColor Cyan
$perConsumer | ForEach-Object { Write-Host ("    {0,-24} {1,4} projitems   CPM={2}" -f $_.Consumer, $_.Imports, $_.CPM) }

if ($unparsed.Count) {
    Write-Host ''
    Write-Host "$($unparsed.Count) declaration(s) COULD NOT BE COMPARED - treat as unknown, not as clean:" -ForegroundColor Magenta
    $unparsed | Format-Table -AutoSize | Out-String | Write-Host
}

if (-not $findings.Count) {
    Write-Host ''
    if ($unparsed.Count) {
        # Never print a green line under a magenta one. An unparseable declaration is exactly where a
        # downgrade hides, and "no comparable findings" beside "1 could not be compared" reads as a
        # clean run to everyone who skims - the failure audit-dependencies.ps1's header warns about,
        # found in this script by its own fixture before it was ever trusted.
        Write-Host "No COMPARABLE finding - but $($unparsed.Count) declaration(s) above could not be checked." -ForegroundColor Magenta
        Write-Host 'That is an unknown result, not a clean one. Resolve those versions and re-run.' -ForegroundColor Magenta
        if ($FailOnFinding) { exit 1 }
        exit 0
    }
    Write-Host 'No consumer declares a framework-owned package. Nothing below, nothing duplicated.' -ForegroundColor Green
    exit 0
}

$below  = @($findings | Where-Object { $_.Verdict -eq 'BELOW' })
$pinned = @($findings | Where-Object { $_.Verdict -eq 'PINNED' })
$equal  = @($findings | Where-Object { $_.Verdict -eq 'EQUAL' })
$higher = @($findings | Where-Object { $_.Verdict -eq 'HIGHER-VIA-INCLUDE' })

Write-Host ''
Write-Host ("=== $($findings.Count) finding(s): $($below.Count) BELOW, $($pinned.Count) PINNED, " +
            "$($equal.Count) EQUAL, $($higher.Count) HIGHER-VIA-INCLUDE") -ForegroundColor Yellow
$findings | Sort-Object Verdict, Consumer, Package | Format-Table -AutoSize | Out-String | Write-Host

if ($below.Count) {
    Write-Host 'BELOW  - policy violation. The consumer is older than the code it compiles in.' -ForegroundColor Red
    Write-Host '         Remove the declaration and take the framework version. Re-pinning is not an option:' -ForegroundColor Red
    Write-Host '         if the newer major breaks the consumer, fix forward or lower the FRAMEWORK declaration.' -ForegroundColor Red
}
if ($pinned.Count) {
    Write-Host 'PINNED - the framework floats here and the consumer does not, so the consumer is frozen at' -ForegroundColor Yellow
    Write-Host '         one version and a published advisory no longer heals on the next restore. Not below' -ForegroundColor Yellow
    Write-Host '         the floor, so not a violation - but it opts out of the reason the float exists, and' -ForegroundColor Yellow
    Write-Host '         that belongs in a written decision rather than in a line nobody remembers adding.' -ForegroundColor Yellow
}
if ($equal.Count) {
    Write-Host 'EQUAL  - redundant. Same floor on both sides, so this is pure NU1504. Delete the line and' -ForegroundColor Yellow
    Write-Host '         leave a comment naming the owning projitems, per CLAUDE-maintenance.md shape 2.' -ForegroundColor Yellow
}
if ($higher.Count) {
    Write-Host 'HIGHER-VIA-INCLUDE - the intent is allowed, the mechanism is not: a second Include is NU1504,' -ForegroundColor Yellow
    Write-Host '         not an override. Use <PackageReference Update="..." Version="..." /> AFTER the' -ForegroundColor Yellow
    Write-Host '         $(BirkoSrc) imports - an Update placed before them is a SILENT no-op.' -ForegroundColor Yellow
}

if ($FailOnFinding) { exit 1 }
exit 0
