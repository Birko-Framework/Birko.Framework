#!/usr/bin/env dotnet
#:project tools/AuditCommon/AuditCommon.csproj

// audit-declarations — does every shared project account for the external packages it uses?
//
// Run:  dotnet run audit-declarations.cs
//       dotnet run audit-declarations.cs -- --root <path-to-repo-root>
//
// Sibling of audit-dependencies.cs, which asks a different question (are any resolved packages
// vulnerable). This one asks whether a Birko.X shared project that `using`s an external library either
// DECLARES it or DOCUMENTS why it does not. Both are the rule from CLAUDE-maintenance.md
// section "External dependencies", settled in TASK-234:
//
//   - the project that WRAPS a library owns it, declared and floating within its major;
//   - a sibling built on that project records the pairing in a comment (declaring twice is NU1504);
//   - a project using the same library independently documents it as consumer-supplied;
//   - a FrameworkReference is never owned.
//
// A comment mentioning TASK-234 and naming the package counts as accounted for. That matters: without it
// this script reported 25 findings against 25 projects that were all following the rule, which is the
// failure mode this codebase keeps meeting - a checker that cannot tell compliance from the defect.
//
// TWO THINGS TO KNOW BEFORE TRUSTING A ZERO:
//
// 1. The map below is namespace-root -> package id, written out EXPLICITLY, and it is the limit of what
//    this script can see. A namespace with no entry is invisible: TASK-234's own count of 38 missed
//    Microsoft.AspNetCore.Authentication.JwtBearer for exactly that reason, and it was found by reading
//    usings by hand. When adding a dependency to the framework, add its namespace here too.
// 2. Do not shortcut the map by treating a namespace root as a package id. `Raven.*` ships in
//    RavenDB.Client and `NpgsqlTypes` in Npgsql, and guessing inflated the first survey from 38 to 43.
//
// Verify the check can fail before believing it: delete a TASK-234 comment from any satellite projitems
// and re-run - it must report exactly that project.
//
// PORTED FROM PowerShell 2026-09-19 (TASK-476). The original's `-notmatch '\\(obj|bin)\\'` tested for
// BACKSLASH-delimited segments, so on Linux it never matched and the scan silently admitted generated
// `obj/**/*.cs` — inventing usings that no hand-written source contains. Path handling now lives once,
// in tools/AuditCommon/Paths.cs.

using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using AuditCommon;

// `$PSScriptRoot`. Baked at compile time from the real source path, which is what a file-based app
// has instead of a runtime script location — AppContext.BaseDirectory points at the build cache.
static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

var cli = Cli.ParseOrExit(args, """
    Usage: dotnet run audit-declarations.cs [-- <options>]

      --root <path>        The framework repo root holding the Birko.* project directories.
                           Defaults to one level above this script.
      --fail-on-finding    Exit 1 when anything is found.
      --help               This text.
    """);
var root = cli.ResolveRoot(ScriptDirectory(), levelsUp: 1);

// namespace root => package ids that satisfy it (any one counts as declared)
var map = new Dictionary<string, string[]>(StringComparer.Ordinal)
{
    ["Nest"] = ["NEST"],
    ["Elasticsearch"] = ["NEST", "Elasticsearch.Net"],
    ["MongoDB"] = ["MongoDB.Driver", "MongoDB.Bson"],
    ["StackExchange"] = ["StackExchange.Redis"],
    ["Microsoft.AspNetCore"] = ["Microsoft.AspNetCore.App"],
    ["Microsoft.Azure.Cosmos"] = ["Microsoft.Azure.Cosmos"],
    ["Raven"] = ["RavenDB.Client"],
    ["Grpc"] = ["Grpc.Net.Client", "Grpc.AspNetCore", "Grpc.Core.Api"],
    ["Microsoft.IdentityModel"] = ["Microsoft.IdentityModel.Tokens", "Microsoft.IdentityModel.JsonWebTokens"],
    ["InfluxDB"] = ["InfluxDB.Client"],
    ["Npgsql"] = ["Npgsql"],
    ["NpgsqlTypes"] = ["Npgsql"],
    ["MQTTnet"] = ["MQTTnet"],
    ["System.IdentityModel.Tokens.Jwt"] = ["System.IdentityModel.Tokens.Jwt"],
    ["Newtonsoft"] = ["Newtonsoft.Json"],
    ["ProtoBuf"] = ["protobuf-net"],
    ["YamlDotNet"] = ["YamlDotNet"],
    ["Microsoft.Data.Sqlite"] = ["Microsoft.Data.Sqlite"],
    ["Microsoft.Data.SqlClient"] = ["Microsoft.Data.SqlClient"],
    ["MySql"] = ["MySql.Data", "MySqlConnector"],
    ["Amazon"] = ["AWSSDK.Core", "AWSSDK.S3"],
    ["Google"] = ["Google.Cloud.Storage.V1", "Google.Protobuf"],
    ["Minio"] = ["Minio"],
    ["Azure"] = ["Azure.Storage.Blobs", "Azure.Messaging.ServiceBus"],
    ["Confluent"] = ["Confluent.Kafka"],
    ["RabbitMQ"] = ["RabbitMQ.Client"],
    ["Avalonia"] = ["Avalonia"],
    ["OpenTelemetry"] = ["OpenTelemetry"],
    ["MessagePack"] = ["MessagePack"],
    ["SkiaSharp"] = ["SkiaSharp"],
};

// The original iterated a PowerShell hashtable and `break`ed on the first matching key. Hashtable
// enumeration order is unspecified, so with two keys able to match one namespace the answer was
// order-dependent. Longest key first makes it the MOST SPECIFIC match, deterministically — same
// answer as today's map, and no longer a coin-flip if a future entry overlaps an existing one.
var keysBySpecificity = map.Keys.OrderByDescending(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).ToArray();

var usingPattern = new Regex(@"^\s*(global\s+)?using\s+(static\s+)?([A-Za-z_][A-Za-z0-9_.]*)\s*;", RegexOptions.Compiled);

var rows = new List<(string Project, string Package, string Via)>();

var projectDirectories = Directory.EnumerateDirectories(root, "Birko.*").OrderBy(d => d, StringComparer.Ordinal).ToArray();
if (projectDirectories.Length == 0)
    throw new InvalidOperationException($"No Birko.* project directories under {root} — wrong root, not a clean result.");

foreach (var directory in projectDirectories)
{
    var projitems = Directory.EnumerateFiles(directory, "*.projitems").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
    if (projitems is null) continue;

    var declared = File.ReadAllText(projitems);

    var usings = new HashSet<string>(StringComparer.Ordinal);
    foreach (var source in Paths.EnumerateFiles(directory, "*.cs"))
    {
        foreach (var line in File.ReadLines(source))
        {
            var match = usingPattern.Match(line);
            if (match.Success) usings.Add(match.Groups[3].Value);
        }
    }

    foreach (var ns in usings.OrderBy(u => u, StringComparer.Ordinal))
    {
        var key = keysBySpecificity.FirstOrDefault(k => ns == k || ns.StartsWith(k + ".", StringComparison.Ordinal));
        if (key is null) continue;

        var packages = map[key];
        var isDeclared = false;
        foreach (var package in packages)
        {
            // Include="Pkg" as PackageReference or FrameworkReference
            if (declared.Contains($@"Include=""{package}""", StringComparison.Ordinal)) isDeclared = true;

            // ...or a TASK-234 comment naming the package: a satellite whose base declares it, or
            // a documented consumer-supplied carve-out. Both are the rule being FOLLOWED, and a
            // scan that cannot tell them from the defect reports 25 findings where there are none.
            if (declared.Contains("TASK-234", StringComparison.Ordinal) &&
                declared.Contains(package, StringComparison.Ordinal)) isDeclared = true;
        }

        if (!isDeclared)
            rows.Add((Path.GetFileName(directory), string.Join(" | ", packages), ns));
    }
}

var distinct = rows
    .Select(r => (r.Project, r.Package))
    .Distinct()
    .OrderBy(r => r.Package, StringComparer.Ordinal)
    .ThenBy(r => r.Project, StringComparer.Ordinal)
    .ToArray();

var projectCount = distinct.Select(r => r.Project).Distinct().Count();

Report.Say($"=== Undeclared: {distinct.Length} (project,package) pairs across {projectCount} projects");
foreach (var group in distinct.GroupBy(r => r.Package).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
    Report.Say($"{group.Key,-45} {group.Count()}");

Report.Say();
Report.Say("=== Detail");
foreach (var row in distinct)
    Report.Say($"{row.Package,-45} {row.Project}");

return cli.FailOnFinding && distinct.Length > 0 ? 1 : 0;
