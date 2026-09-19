#!/usr/bin/env dotnet
#:project tools/AuditCommon/AuditCommon.csproj

// audit-dependencies — sweeps the whole Birko family for vulnerable NuGet packages, transitives included.
//
// Run:  dotnet run audit-dependencies.cs
//       dotnet run audit-dependencies.cs -- --fail-on-finding     # exit 1 — for a scheduled job
//       dotnet run audit-dependencies.cs -- --root <birko-checkout-root>
//
// NuGet's audit is ALREADY ON by default in the .NET SDK (NuGetAudit=true, NuGetAuditMode=all,
// NuGetAuditLevel=low — verified 2026-08-17), so every ordinary `dotnet build` already prints
// NU1901-NU1904 for an affected project. This script does not enable anything. It exists because
// of the two gaps a build-time warning cannot close:
//
//   1. A build only audits what you build. An advisory published against a project nobody has
//      touched for a month is printed by nobody.
//   2. The warning is printed and scrolls past. Only `verify-conventions` check 1 promotes it to
//      an error, and only for the diff of the task in hand.
//
// So this is a PERIODIC check, not a build setting. Run it on a schedule, before a release, and
// after any dependency bump.
//
// Scope matters and is the reason this sweeps consumers too: TASK-230 found that a test-only sweep
// reported SQLitePCLRaw 2.1.10 and implied "anything newer is fine", while Birko.Sandbox — on a
// NEWER Microsoft.Data.Sqlite — was still affected at 2.1.11. A sweep scoped to one tree produced
// a fix that looked complete and was not.
//
// --root  The Birko checkout root that holds the Framework / Consumers buckets. Test projects live
//         inside the framework repo at Framework/tests, so that is swept as part of Framework.
//         Defaults to two levels above this script (.../Birko/Framework/Birko.Framework -> .../Birko).
//
// PORTED FROM PowerShell 2026-09-19 (TASK-476), and the port CLOSES TASK-474's leftover. See the
// bucket guard below: the original threw only when EVERY bucket was missing, which is why a
// mistyped `Framework\tests` — and, on Linux, a correctly-typed one — could drop the entire tests/
// tree while `Consumers` still resolved, and the sweep reported 81 of 248 projects as a whole-tree
// result. "A guard for 'none' is not a guard for 'fewer than asked for.'"

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using AuditCommon;

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

var cli = Cli.ParseOrExit(args, """
    Usage: dotnet run audit-dependencies.cs [-- <options>]

      --root <path>        Birko checkout root holding the Framework / Consumers buckets.
                           Defaults to two levels above this script.
      --fail-on-finding    Exit 1 when anything is found. Use this for a scheduled job.
      --help               This text.
    """);
var root = cli.ResolveRoot(ScriptDirectory(), levelsUp: 2);

// ⚠ EVERY declared bucket must resolve. Missing ones are named individually and the run stops —
// this is the TASK-474 defect, and it is also exactly how this script failed on Linux, where the
// separator in a hard-coded "Framework\tests" is an ordinary filename character. The bucket parts
// are joined by the OS, never written with a literal separator.
var declaredBuckets = new[]
{
    Path.Combine(root, "Framework", "tests"),
    Path.Combine(root, "Consumers"),
};

var missing = declaredBuckets.Where(b => !Directory.Exists(b)).ToArray();
if (missing.Length > 0)
{
    Report.Unknown($"{missing.Length} of {declaredBuckets.Length} declared bucket(s) do not exist under {root}:");
    foreach (var bucket in missing) Report.Unknown($"    {bucket}");
    Report.Unknown("    A partial sweep is not a clean sweep. Fix the root and re-run.");
    return 1;
}

// Shared projects (.shproj/.projitems) cannot restore on their own — they are audited through the
// projects that import them, which is why only real .csproj files are swept.
var projects = declaredBuckets
    .SelectMany(b => Paths.EnumerateFiles(b, "*.csproj"))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToArray();

Report.Heading($"Auditing {projects.Length} projects under {root}");

var rowPattern = new Regex(@"^\s+>\s+(\S+)\s+(.*?)\s+(Low|Moderate|High|Critical)\s+(\S+)\s*$", RegexOptions.Compiled);
var failurePattern = new Regex(@"error NU\d+|Failed to restore|error MSB\d+|not a valid project", RegexOptions.Compiled);
var reasonPattern = new Regex(@"error (NU\d+|MSB\d+)", RegexOptions.Compiled);

var findings = new List<(string Project, string Package, string Version, string Severity, string Advisory)>();
var unauditable = new List<(string Project, string Reason)>();

for (var i = 0; i < projects.Length; i++)
{
    var project = projects[i];
    var name = Path.GetFileName(project);

    if (!Console.IsOutputRedirected)
        Console.Error.Write($"\r[{i + 1}/{projects.Length}] {name}".PadRight(90));

    var output = RunDotnet("list", project, "package", "--vulnerable", "--include-transitive");

    // A project whose restore fails emits NO vulnerability rows, which is indistinguishable from a clean
    // one unless you look. Measured the hard way: floating InfluxDB.Client pulled a Newtonsoft.Json
    // requirement that conflicted with Birko.Sandbox's pin, restore failed with NU1605, and this script
    // reported the sandbox as having no findings — while it still had four. A checker that reads
    // "could not check" as "nothing to report" is worse than no checker, because it is trusted.
    if (failurePattern.IsMatch(output))
    {
        var codes = reasonPattern.Matches(output).Select(m => m.Groups[1].Value).Distinct().ToArray();
        unauditable.Add((name, codes.Length > 0 ? string.Join(", ", codes) : "restore failed"));
        continue;
    }

    foreach (var line in output.Split('\n'))
    {
        var match = rowPattern.Match(line.TrimEnd('\r'));
        if (!match.Success) continue;

        // Split on ANY whitespace, as the original's `-split '\s+'` did. `dotnet list` pads these
        // columns with spaces today; a tab would silently make the requested version the resolved one.
        var versions = match.Groups[2].Value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        findings.Add((
            name,
            match.Groups[1].Value,
            versions.Length > 0 ? versions[^1] : "",
            match.Groups[3].Value,
            match.Groups[4].Value));
    }
}

if (!Console.IsOutputRedirected) Console.Error.Write("\r".PadRight(90) + "\r");

void WriteUnauditable()
{
    if (unauditable.Count == 0) return;
    Report.Say();
    Report.Unknown($"{unauditable.Count} project(s) COULD NOT BE AUDITED — treat as unknown, not as clean:");
    foreach (var item in unauditable) Report.Unknown($"    {item.Project}  ({item.Reason})");
    Report.Unknown("    A failed restore yields no vulnerability rows. Fix the restore, then re-run.");
}

if (findings.Count == 0)
{
    var checkedCount = projects.Length - unauditable.Count;
    if (unauditable.Count > 0)
    {
        Report.Good($"No vulnerable packages found across {checkedCount} audited project(s).");
        WriteUnauditable();
        // Unknown is not success: an unauditable project is exactly where a finding hides.
        return cli.FailOnFinding ? 1 : 0;
    }

    Report.Good($"No vulnerable packages found across {projects.Length} projects.");
    return 0;
}

// Group by package: one advisory usually spans many projects, and the count is the remediation cost.
var rank = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["Critical"] = 0, ["High"] = 1, ["Moderate"] = 2, ["Low"] = 3,
};

Report.Say();
Report.Warn($"{findings.Count} finding(s) across {findings.Select(f => f.Project).Distinct().Count()} project(s):");

var grouped = findings
    .GroupBy(f => (f.Package, f.Version, f.Severity))
    .OrderBy(g => rank.TryGetValue(g.Key.Severity, out var r) ? r : int.MaxValue)
    .ThenByDescending(g => g.Count())
    .ThenBy(g => g.Key.Package, StringComparer.Ordinal);

foreach (var group in grouped)
{
    var first = group.First();
    Report.Say();
    var line = $"  {first.Package} {first.Version}  [{first.Severity}]  x{group.Count()} project(s)";
    if (first.Severity is "Critical" or "High") Report.Bad(line); else Report.Warn(line);
    Report.Faint($"    {first.Advisory}");
    foreach (var project in group.Select(g => g.Project).Distinct().OrderBy(p => p, StringComparer.Ordinal))
        Report.Faint($"      {project}");
}

WriteUnauditable();

Report.Say();
Report.Heading("Remediation: prefer bumping the TOP-LEVEL package that pulls the vulnerable one");
Report.Heading("(`dotnet nuget why <project> <package>` shows the chain); pin only when no bump clears it.");
Report.Heading("Check the RESOLVED transitive, not the top-level number — a newer top-level can still be affected.");

return cli.FailOnFinding ? 1 : 0;

static string RunDotnet(params string[] arguments)
{
    var info = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);

    using var process = Process.Start(info)!;
    var combined = new StringBuilder();

    // Read both streams concurrently: `dotnet list` writes restore errors to stderr, and draining
    // them one after the other can deadlock on a full pipe buffer for a project with a long error list.
    var stderrTask = process.StandardError.ReadToEndAsync();
    combined.Append(process.StandardOutput.ReadToEnd());
    combined.Append(stderrTask.GetAwaiter().GetResult());
    process.WaitForExit();

    return combined.ToString();
}
