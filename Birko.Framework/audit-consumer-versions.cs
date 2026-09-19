#!/usr/bin/env dotnet
#:project tools/AuditCommon/AuditCommon.csproj

// audit-consumer-versions — does any consumer declare a package version BELOW what the framework declares?
//
// Run:  dotnet run audit-consumer-versions.cs
//       dotnet run audit-consumer-versions.cs -- --fail-on-finding
//       dotnet run audit-consumer-versions.cs -- --root <birko-checkout-root>
//
// The third audit sibling. audit-declarations asks whether every shared project ACCOUNTS for the
// packages it uses; audit-dependencies asks whether any resolved package is VULNERABLE. This one asks
// the consumer-side question that completes the ownership rule in CLAUDE-maintenance.md section
// "External dependencies":
//
//     A consumer must never declare a package version LOWER than the framework declares.
//     Higher is allowed - the consumer then owns any breakage. Equal means the consumer
//     should not declare it at all.
//
// WHY THIS NEEDS A SCRIPT AT ALL, WHICH IS THE ONLY INTERESTING THING ABOUT IT:
//
// That rule is exactly NuGet's NU1605 "detected package downgrade", which is an ERROR by default
// and costs nothing. But NU1605 can only compare across a PACKAGE DEPENDENCY EDGE. Birko ships
// shared projects: a .projitems is compiled INTO the consumer's assembly and has no package
// identity, so there is no edge, and the framework's declaration and the consumer's are simply two
// PackageReference items in one project file. That is NU1504 - a warning, about duplication, with
// nothing to say about which version is lower. The guard exists, is free, and is blind to precisely
// the shape the framework ships in. Measured 2026-09-19 on Symbio: Npgsql 9.* against the
// framework's 10.*, sitting in the build for weeks, reported only as a duplicate.
//
// Shipping the backends as real NuGet packages would delete this script. That is deferred until the
// libraries stabilise (TASK-234 section "Out of scope"), so until then the rule is enforced here.
//
// A CONSUMER HAS TWO PLACES TO WRITE A VERSION, AND BOTH ARE CHECKED. Without central package
// management the version is on the project's own PackageReference. With it, the project declares a
// bare Include and the version lives in Directory.Packages.props - so a csproj scan alone sees
// nothing and reports the consumer clean. Symbio adopted CPM on 2026-09-19 and that is exactly what
// happened on the first run afterwards; its 16 entries were correct, but the zero was arrived at by
// looking in the wrong file. Both passes run, and each finding names the file that holds the version.
//
// THREE THINGS TO KNOW BEFORE TRUSTING A ZERO:
//
// 1. Only $(BirkoSrc)-rooted imports are followed. A consumer that reaches the framework through a
//    different property, a relative path or a symlink is INVISIBLE to this script - it will be
//    reported as having no framework imports, which reads exactly like compliance. The summary
//    prints the per-consumer import count for that reason: a consumer you know imports Birko and
//    that shows 0 is a defect in this script, not a clean result.
// 2. Only FLOORS are compared. `10.*` is floor 10.0.0; what it resolves to today is not consulted,
//    deliberately - the policy is about what a project DECLARES, and a resolved version moves under
//    you on the next restore.
// 3. Only a project that BOTH imports a .projitems AND declares the package is checked. A sibling
//    project in the same repo declaring an older version reaches the framework through a
//    ProjectReference, where NuGet's own NU1605 does see it and does fail the build. Out of scope
//    here on purpose, not overlooked.
//
// Verify the check can fail before believing it: change any consumer declaration to a lower major
// and re-run - it must report exactly that project and package as BELOW.
//
// PORTED FROM PowerShell 2026-09-19 (TASK-476). ⚠ This script was the worst Linux casualty of the
// four, and it failed silently: consumer imports are written `$(BirkoSrc)\Birko.Helpers\...` — the
// MSBuild file format, backslash-separated on every platform — so after substitution the path could
// not be opened, the import graph came back empty, and EVERY consumer reported 0 imports. That is
// the exact state note 1 above calls "a defect in this script, not a clean result". Separator
// handling now has one producer, tools/AuditCommon/Paths.cs.

using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using AuditCommon;

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

var cli = Cli.ParseOrExit(args, """
    Usage: dotnet run audit-consumer-versions.cs [-- <options>]

      --root <path>        Birko checkout root holding the Framework / Consumers buckets.
                           Defaults to two levels above this script.
      --fail-on-finding    Exit 1 when anything is found. Use this when wiring it into a gate.
      --help               This text.
    """);
var root = cli.ResolveRoot(ScriptDirectory(), levelsUp: 2);

var frameworkBucket = Path.Combine(root, "Framework");
var consumerBucket = Path.Combine(root, "Consumers");
foreach (var bucket in new[] { frameworkBucket, consumerBucket })
{
    if (!Directory.Exists(bucket)) throw new DirectoryNotFoundException($"Bucket not found: {bucket}");
}

var commentPattern = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.Compiled);
string StripXmlComments(string text) => commentPattern.Replace(text, "");

var packageRefPattern = new Regex(@"<PackageReference\s+Include=""([^""]+)""([^>/]*)", RegexOptions.Compiled);
var packageRefEitherPattern = new Regex(@"<PackageReference\s+(Include|Update)=""([^""]+)""([^>/]*)", RegexOptions.Compiled);
var packageVersionPattern = new Regex(@"<PackageVersion\s+Include=""([^""]+)""[^>]*Version=""([^""]+)""", RegexOptions.Compiled);
var importPattern = new Regex(@"<Import\s+Project=""([^""]+)""", RegexOptions.Compiled);
var versionAttributePattern = new Regex(@"Version=""([^""]+)""", RegexOptions.Compiled);
var centrallyManagedPattern = new Regex(@"ManagePackageVersionsCentrally\)'\s*==\s*'true'", RegexOptions.Compiled);
var bracketedLowerBoundPattern = new Regex(@"^[\[\(]\s*([0-9][0-9.]*)", RegexOptions.Compiled);
var numericVersionPattern = new Regex(@"^[0-9]+(\.[0-9]+)*$", RegexOptions.Compiled);

// A declared version -> a comparable floor. Returns null when the shape is not understood, and the
// caller reports that as UNPARSEABLE rather than assuming it is fine: this is the one place a wrong
// guess would turn a downgrade into a clean line.
Version? VersionFloor(string? declared)
{
    if (string.IsNullOrWhiteSpace(declared)) return null;

    var s = declared.Trim();
    if (s == "*") return new Version(0, 0, 0, 0);                        // latest stable; never below anything

    var bracketed = bracketedLowerBoundPattern.Match(s);
    if (bracketed.Success) s = bracketed.Groups[1].Value;                // [10.0,) / [9.0.3] -> lower bound

    var dash = s.IndexOf('-');
    if (dash >= 0) s = s[..dash];                                        // drop any prerelease suffix
    s = s.Replace("*", "0");                                             // 10.* -> 10.0

    if (!numericVersionPattern.IsMatch(s)) return null;

    // The regex admits a component too large for an int ("99999999999999"), which the PowerShell
    // original crashed on. Returning null routes it to UNPARSEABLE instead — the caller already knows
    // how to say "could not be compared", and that is the honest answer for a shape we cannot read.
    var parts = new int[4];
    var components = s.Split('.');
    for (var i = 0; i < 4; i++)
    {
        if (i >= components.Length) { parts[i] = 0; continue; }
        if (!int.TryParse(components[i], out parts[i])) return null;
    }

    return new Version(parts[0], parts[1], parts[2], parts[3]);
}

// ---- 1. what the framework declares --------------------------------------------------------------
// Only the non-CPM half of each conditioned pair carries a version, and that pair is ONE declaration.
// Counting both halves makes every package look duplicated - the first thing that went wrong when
// this was measured by hand.
var frameworkDeclarations = new Dictionary<string, (string Version, string Owner)>(StringComparer.Ordinal);

foreach (var directory in Directory.EnumerateDirectories(frameworkBucket, "Birko.*").OrderBy(d => d, StringComparer.Ordinal))
{
    foreach (var projitems in Directory.EnumerateFiles(directory, "*.projitems").OrderBy(f => f, StringComparer.Ordinal))
    {
        var owner = Path.GetFileNameWithoutExtension(projitems);
        var text = StripXmlComments(File.ReadAllText(projitems));

        foreach (Match match in packageRefPattern.Matches(text))
        {
            var rest = match.Groups[2].Value;
            if (centrallyManagedPattern.IsMatch(rest)) continue;

            var version = versionAttributePattern.Match(rest);
            if (!version.Success) continue;

            frameworkDeclarations[match.Groups[1].Value] = (version.Groups[1].Value, owner);
        }
    }
}

// ---- 2. one consumer project's $(BirkoSrc) import graph, followed transitively --------------------
HashSet<string> ImportedProjitems(string projectFile, string bucket)
{
    var result = new HashSet<string>(StringComparer.Ordinal);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var queue = new Queue<string>();
    queue.Enqueue(projectFile);

    while (queue.Count > 0)
    {
        var file = queue.Dequeue();
        if (!File.Exists(file)) continue;

        var full = Path.GetFullPath(file);
        if (!seen.Add(Paths.IdentityKey(full))) continue;

        var text = StripXmlComments(File.ReadAllText(full));
        foreach (Match match in importPattern.Matches(text))
        {
            var raw = match.Groups[1].Value
                .Replace("$(BirkoSrc)", bucket, StringComparison.Ordinal)
                .Replace("$(MSBuildThisFileDirectory)", Path.GetDirectoryName(full) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

            if (raw.Contains("$(", StringComparison.Ordinal)) continue;
            if (!raw.EndsWith(".projitems", StringComparison.OrdinalIgnoreCase)) continue;

            // ⚠ The MSBuild file format is backslash-separated on every platform. Without this the
            // path is unopenable on Linux and the whole graph comes back empty — see the header.
            var resolved = Paths.FromMsBuild(raw);

            result.Add(resolved);
            queue.Enqueue(resolved);
        }
    }

    return result;
}

// ---- 3. compare -----------------------------------------------------------------------------------
var findings = new List<(string Consumer, string Project, string Package, string Declares, string Framework, string Verdict)>();
var unparsed = new List<(string Consumer, string Project, string Package, string ConsumerVersion, string FrameworkVersion)>();
var perConsumer = new List<(string Consumer, int Imports, string Cpm, string Central)>();

foreach (var consumerDirectory in Directory.EnumerateDirectories(consumerBucket).OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
{
    var consumerName = Path.GetFileName(consumerDirectory);
    var cpmFile = Path.Combine(consumerDirectory, "Directory.Packages.props");
    var isCpm = File.Exists(cpmFile);

    var central = new Dictionary<string, string>(StringComparer.Ordinal);
    if (isCpm)
    {
        var centralText = StripXmlComments(File.ReadAllText(cpmFile));
        foreach (Match match in packageVersionPattern.Matches(centralText))
            central[match.Groups[1].Value] = match.Groups[2].Value;
    }

    var projects = Paths.EnumerateFiles(consumerDirectory, "*.csproj")
        .Where(p => !Paths.IsUnderAny(p, "node_modules"))
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToArray();

    var importCount = 0;
    var consumerInherited = new Dictionary<string, (string Version, string Owner)>(StringComparer.Ordinal);

    foreach (var project in projects)
    {
        var projectText = StripXmlComments(File.ReadAllText(project));
        if (!projectText.Contains("$(BirkoSrc)", StringComparison.Ordinal)) continue;

        var imported = ImportedProjitems(project, frameworkBucket);
        importCount += imported.Count;

        // which framework-owned packages does THIS project inherit?
        var inherited = new Dictionary<string, (string Version, string Owner)>(StringComparer.Ordinal);
        foreach (var projitems in imported)
        {
            var owner = Path.GetFileNameWithoutExtension(projitems);
            foreach (var (package, declaration) in frameworkDeclarations)
            {
                if (!string.Equals(declaration.Owner, owner, StringComparison.Ordinal)) continue;
                inherited[package] = declaration;
                consumerInherited[package] = declaration;
            }
        }

        foreach (Match match in packageRefEitherPattern.Matches(projectText))
        {
            var verb = match.Groups[1].Value;
            var package = match.Groups[2].Value;
            if (!inherited.TryGetValue(package, out var frameworkDeclaration)) continue;

            var ownVersionMatch = versionAttributePattern.Match(match.Groups[3].Value);
            var own = ownVersionMatch.Success ? ownVersionMatch.Groups[1].Value : null;

            // Under CPM a project's PackageReference carries no version by design, so this item says
            // nothing about WHICH version and everything about there being a second Include beside the
            // framework's - NU1504, and nothing more. Resolving it against the central file and calling
            // it BELOW reported one fact twice and named the wrong file as the place to fix it:
            // BardStudio.Birko.csproj was printed as "Include=9.0.3" while containing no version at all.
            // The version question belongs to the Directory.Packages.props pass further down.
            if (isCpm && own is null)
            {
                findings.Add((consumerName, Path.GetFileName(project), package, $"{verb}=(central)",
                    $"{frameworkDeclaration.Version} ({frameworkDeclaration.Owner})", "EQUAL"));
                continue;
            }

            var frameworkVersion = frameworkDeclaration.Version;
            var consumerFloor = VersionFloor(own);
            var frameworkFloor = VersionFloor(frameworkVersion);

            if (consumerFloor is null || frameworkFloor is null)
            {
                unparsed.Add((consumerName, Path.GetFileName(project), package, own ?? "", frameworkVersion));
                continue;
            }

            // Floors alone are not the whole story. The framework floats deliberately, so that a
            // published advisory heals on the next restore; a consumer that declares an EXACT version
            // freezes it, and does so invisibly when the floor happens to match. DraCode's
            // `Microsoft.Data.Sqlite 9.0.4` and `JwtBearer 10.0.0` are both that shape, and a
            // floor-only comparison called the second one identical to `10.*`.
            var frameworkFloats = frameworkVersion.Contains('*', StringComparison.Ordinal);
            var ownFloats = own is not null && own.Contains('*', StringComparison.Ordinal);

            string? verdict = null;
            if (consumerFloor < frameworkFloor) verdict = "BELOW";
            else if (frameworkFloats && !ownFloats) verdict = "PINNED";
            else if (verb != "Update") verdict = consumerFloor == frameworkFloor ? "EQUAL" : "HIGHER-VIA-INCLUDE";

            if (verdict is null) continue;

            findings.Add((consumerName, Path.GetFileName(project), package, $"{verb}={own}",
                $"{frameworkVersion} ({frameworkDeclaration.Owner})", verdict));
        }
    }

    // ---- CPM: the version is NOT in the csproj, so the loop above cannot see it ------------------
    // Under central management the framework's own declaration is the bare half of its conditioned
    // pair - <PackageReference Include="Npgsql" /> with no version - and the version comes from the
    // consumer's Directory.Packages.props. The consumer's project files then declare nothing at all,
    // so everything above compares nothing and the consumer reports clean.
    //
    // ⚠ That is the hole this block exists for, and it went live the day Symbio adopted CPM
    // (its TASK-738, 2026-09-19). A downgrade written as a PackageVersion is invisible to a csproj
    // scan while being every bit as effective as one written as a PackageReference. Symbio's 16
    // entries happened to be correct; the check reported that for the wrong reason, which is a zero
    // nobody should have trusted.
    //
    // Note EQUAL is NOT a finding here, unlike in the csproj case: under CPM the central entry is
    // REQUIRED for every inherited package, and its absence is NU1010 at restore.
    if (isCpm && consumerInherited.Count > 0)
    {
        foreach (var package in consumerInherited.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var frameworkVersion = consumerInherited[package].Version;

            if (!central.TryGetValue(package, out var own))
            {
                findings.Add((consumerName, "Directory.Packages.props", package, "(absent)",
                    $"{frameworkVersion} ({consumerInherited[package].Owner})", "MISSING-CENTRAL"));
                continue;
            }

            var consumerFloor = VersionFloor(own);
            var frameworkFloor = VersionFloor(frameworkVersion);
            if (consumerFloor is null || frameworkFloor is null)
            {
                unparsed.Add((consumerName, "Directory.Packages.props", package, own, frameworkVersion));
                continue;
            }

            string? verdict = null;
            if (consumerFloor < frameworkFloor) verdict = "BELOW";
            else if (frameworkVersion.Contains('*', StringComparison.Ordinal) && !own.Contains('*', StringComparison.Ordinal)) verdict = "PINNED";
            if (verdict is null) continue;

            findings.Add((consumerName, "Directory.Packages.props", package, $"PackageVersion={own}",
                $"{frameworkVersion} ({consumerInherited[package].Owner})", verdict));
        }
    }

    if (importCount > 0)
    {
        perConsumer.Add((consumerName, importCount, isCpm ? "yes" : "no",
            isCpm ? $"{consumerInherited.Count} inherited" : "-"));
    }
}

// ---- 4. report ------------------------------------------------------------------------------------
var ownerCount = frameworkDeclarations.Values.Select(v => v.Owner).Distinct().Count();

Report.Say();
Report.Heading($"Framework declares {frameworkDeclarations.Count} packages across {ownerCount} shared projects.");
Report.Heading($"Consumers importing them: {perConsumer.Count}");
foreach (var row in perConsumer)
    Report.Say($"    {row.Consumer,-24} {row.Imports,4} projitems   CPM={row.Cpm,-3}  {row.Central}");

if (unparsed.Count > 0)
{
    Report.Say();
    Report.Unknown($"{unparsed.Count} declaration(s) COULD NOT BE COMPARED - treat as unknown, not as clean:");
    Report.Table(
        ["Consumer", "Project", "Package", "Consumer_Version", "Framework_Version"],
        [.. unparsed.Select(u => new[] { u.Consumer, u.Project, u.Package, u.ConsumerVersion, u.FrameworkVersion })]);
}

if (findings.Count == 0)
{
    Report.Say();
    if (unparsed.Count > 0)
    {
        // Never print a green line under a magenta one. An unparseable declaration is exactly where a
        // downgrade hides, and "no comparable findings" beside "1 could not be compared" reads as a
        // clean run to everyone who skims - the failure audit-dependencies' header warns about,
        // found in this script by its own fixture before it was ever trusted.
        Report.Unknown($"No COMPARABLE finding - but {unparsed.Count} declaration(s) above could not be checked.");
        Report.Unknown("That is an unknown result, not a clean one. Resolve those versions and re-run.");
        return cli.FailOnFinding ? 1 : 0;
    }

    Report.Good("No consumer declares a framework-owned package. Nothing below, nothing duplicated.");
    return 0;
}

var below = findings.Where(f => f.Verdict == "BELOW").ToArray();
var pinned = findings.Where(f => f.Verdict == "PINNED").ToArray();
var equal = findings.Where(f => f.Verdict == "EQUAL").ToArray();
var higher = findings.Where(f => f.Verdict == "HIGHER-VIA-INCLUDE").ToArray();
var absent = findings.Where(f => f.Verdict == "MISSING-CENTRAL").ToArray();

Report.Say();
Report.Warn($"=== {findings.Count} finding(s): {below.Length} BELOW, {pinned.Length} PINNED, " +
            $"{equal.Length} EQUAL, {higher.Length} HIGHER-VIA-INCLUDE, {absent.Length} MISSING-CENTRAL");

Report.Table(
    ["Consumer", "Project", "Package", "Declares", "Framework", "Verdict"],
    [.. findings
        .OrderBy(f => f.Verdict, StringComparer.Ordinal)
        .ThenBy(f => f.Consumer, StringComparer.Ordinal)
        .ThenBy(f => f.Package, StringComparer.Ordinal)
        .Select(f => new[] { f.Consumer, f.Project, f.Package, f.Declares, f.Framework, f.Verdict })]);

if (below.Length > 0)
{
    Report.Bad("BELOW  - policy violation. The consumer is older than the code it compiles in.");
    Report.Bad("         Remove the declaration and take the framework version. Re-pinning is not an option:");
    Report.Bad("         if the newer major breaks the consumer, fix forward or lower the FRAMEWORK declaration.");
}
if (pinned.Length > 0)
{
    Report.Warn("PINNED - the framework floats here and the consumer does not, so the consumer is frozen at");
    Report.Warn("         one version and a published advisory no longer heals on the next restore. Not below");
    Report.Warn("         the floor, so not a violation - but it opts out of the reason the float exists, and");
    Report.Warn("         that belongs in a written decision rather than in a line nobody remembers adding.");
}
if (equal.Length > 0)
{
    Report.Warn("EQUAL  - redundant. Same floor on both sides, so this is pure NU1504. Delete the line and");
    Report.Warn("         leave a comment naming the owning projitems, per CLAUDE-maintenance.md shape 2.");
}
if (higher.Length > 0)
{
    Report.Warn("HIGHER-VIA-INCLUDE - the intent is allowed, the mechanism is not: a second Include is NU1504,");
    Report.Warn("         not an override. Use <PackageReference Update=\"...\" Version=\"...\" /> AFTER the");
    Report.Warn("         $(BirkoSrc) imports - an Update placed before them is a SILENT no-op.");
}
if (absent.Length > 0)
{
    Report.Warn("MISSING-CENTRAL - a CPM consumer inherits this package from a .projitems it imports but");
    Report.Warn("         declares no PackageVersion for it. Restore fails NU1010 naming the package, so this");
    Report.Warn("         is loud rather than silent - reported because it is visible here without a restore.");
}

return cli.FailOnFinding ? 1 : 0;
