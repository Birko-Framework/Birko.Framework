#!/usr/bin/env dotnet
// spec-links — writes, or checks, the "Specs" line in every project's CLAUDE.md (TASK-226).
//
// Run:  dotnet run spec-links.cs              rewrite every Specs block from the map
//       dotnet run spec-links.cs -- --check   change nothing; exit 1 and list each CLAUDE.md that disagrees
//
// Run from Birko.Framework/ (or anywhere below the repo root: it walks up to docs/specs/.map.yml).
//
// WHY: every spec lives in Birko.Framework/docs/specs/, and an agent working inside one project never saw them -
// measured 2026-11-04, 1 of 196 project CLAUDE.md files mentioned docs/specs at all. The user decided (TASK-226) that
// there are no per-project spec trees; instead each project's CLAUDE.md names the areas that cover it.
//
// ONE PRODUCER: the map. A project is covered by an area when a file git tracks under the project matches one of the
// area's `sources:` globs. The globs are matched BY GIT as `:(glob)` pathspecs, which is what /specs itself does (its
// SKILL.md: "Match every glob as a git pathspec with :(glob)"), so this script cannot read a glob differently from the
// skill. `ignore:` plays no part: it decides what counts as unmapped, not what an area's sources are - so a project the
// map ignores but an area names explicitly (Birko.Data.ViewModel, in repository-contract) is covered.
//
// A test project (tests/Birko.X.Tests) gets the areas of the project it tests, Birko.X: the map keeps tests out of
// every area on purpose, and an agent in a test project needs the specs of the code under test.
//
// The block sits right under the CLAUDE.md's first `# ` heading, between two HTML comments, and is replaced whole.
// Never edit it by hand: change the map, then run this. CI runs --check (helper-scripts.yml), so a map change or a
// new project that is not re-run fails there.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

var check = args.Contains("--check");

var framework = FindFramework(Directory.GetCurrentDirectory())
    ?? FailValue<string>("Cannot find docs/specs/.map.yml above the current directory. Run from Birko.Framework/.");
var repo = Path.GetFullPath(Path.Combine(framework, ".."));
var areas = ReadAreas(Path.Combine(framework, "docs", "specs", ".map.yml"));
if (areas.Count == 0) Fail("The map has no areas: refusing to write 'none yet' everywhere.");

// project directory name (top-level, e.g. Birko.Data.SQL) -> covering areas
var covering = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
foreach (var (name, sources) in areas)
{
    if (sources.Count == 0) Fail($"Area '{name}' has no sources.");
    foreach (var path in GitLsFiles(framework, sources))
    {
        // paths come back relative to Birko.Framework/: "../Birko.X/...", or "x" for the aggregator's own files
        if (!path.StartsWith("../", StringComparison.Ordinal)) continue;
        var project = path[3..].Split('/')[0];
        if (!covering.TryGetValue(project, out var set)) covering[project] = set = new(StringComparer.Ordinal);
        set.Add(name);
    }
}

var targets = new List<(string ClaudeMd, string Subject, string LinkPrefix, bool IsTest)>();
foreach (var dir in Directory.GetDirectories(repo, "Birko.*").OrderBy(d => d, StringComparer.Ordinal))
{
    var name = Path.GetFileName(dir);
    if (name == "Birko.Framework") continue; // the aggregator: its CLAUDE.md is the index, not a project
    var md = Path.Combine(dir, "CLAUDE.md");
    if (File.Exists(md)) targets.Add((md, name, "../Birko.Framework/docs/specs/", false));
}
var tests = Path.Combine(repo, "tests");
if (Directory.Exists(tests))
{
    foreach (var dir in Directory.GetDirectories(tests).OrderBy(d => d, StringComparer.Ordinal))
    {
        var md = Path.Combine(dir, "CLAUDE.md");
        if (!File.Exists(md)) continue;
        var name = Path.GetFileName(dir);
        var subject = name.EndsWith(".Tests", StringComparison.Ordinal) ? name[..^".Tests".Length] : name;
        targets.Add((md, subject, "../../Birko.Framework/docs/specs/", true));
    }
}

var stale = new List<string>();
var written = 0;
foreach (var (md, subject, prefix, isTest) in targets)
{
    var block = Block(covering.TryGetValue(subject, out var set) ? set : null, subject, prefix, isTest);
    var text = File.ReadAllText(md);
    var updated = WithBlock(text, block);
    if (updated == text) continue;
    stale.Add(Path.GetRelativePath(repo, md).Replace('\\', '/'));
    if (!check) { File.WriteAllText(md, updated); written++; }
}

var coveredCount = targets.Count(t => covering.ContainsKey(t.Subject));
Console.WriteLine($"areas: {areas.Count}, CLAUDE.md files: {targets.Count}, with an area: {coveredCount}, none yet: {targets.Count - coveredCount}");
if (check)
{
    if (stale.Count == 0) { Console.WriteLine("spec-links: every Specs line agrees with the map."); return 0; }
    Console.WriteLine($"spec-links: {stale.Count} CLAUDE.md file(s) disagree with docs/specs/.map.yml. Run `dotnet run spec-links.cs` from Birko.Framework/:");
    foreach (var s in stale) Console.WriteLine($"  {s}");
    return 1;
}
Console.WriteLine($"spec-links: rewrote {written} CLAUDE.md file(s).");
return 0;

static string Block(SortedSet<string>? areas, string subject, string prefix, bool isTest)
{
    var sb = new StringBuilder();
    sb.Append(Marker.Begin).Append('\n');
    if (areas == null || areas.Count == 0)
    {
        sb.Append("**Specs:** none yet. No area in the [spec map](").Append(prefix).Append(".map.yml) covers ")
          .Append(isTest ? $"`{subject}`, the project these tests exercise." : "this project.").Append('\n');
    }
    else
    {
        sb.Append("**Specs**").Append(isTest ? $" (of `{subject}`, the project these tests exercise)" : "").Append(": ")
          .Append(string.Join(" · ", areas.Select(a => $"[{a}]({prefix}{a}.md)"))).Append('\n');
    }
    sb.Append(Marker.End);
    return sb.ToString();
}

static string WithBlock(string text, string block)
{
    var nl = text.Contains("\r\n") ? "\r\n" : "\n";
    var body = block.Replace("\n", nl);
    var start = text.IndexOf(Marker.Begin, StringComparison.Ordinal);
    if (start >= 0)
    {
        var end = text.IndexOf(Marker.End, start, StringComparison.Ordinal);
        if (end < 0) Fail("A Specs block has its begin marker but no end marker: fix it by hand, then re-run.");
        return text[..start] + body + text[(end + Marker.End.Length)..];
    }
    // first "# " heading: the block goes under it, separated by blank lines. The heading stops before the line break:
    // `.*` took the \r of a CRLF file, and the block landed after it as "\r\r\n", which turned every line of 252 files
    // into a diff (git stops normalising a file with mixed line ends).
    var heading = Regex.Match(text, @"^# [^\r\n]*", RegexOptions.Multiline);
    if (!heading.Success) return body + nl + nl + text;
    var after = heading.Index + heading.Length;
    var rest = text[after..].TrimStart('\r', '\n');
    return text[..after] + nl + nl + body + nl + nl + rest;
}

static List<(string Name, List<string> Sources)> ReadAreas(string mapPath)
{
    // The map is hand-edited YAML of a fixed shape (areas: - name / title / sources: - glob). Parsed by its shape
    // rather than with a YAML library, so the script needs no package; anything else under `areas:` fails loudly.
    var areas = new List<(string, List<string>)>();
    var inAreas = false;
    List<string>? sources = null;
    var inSources = false;
    foreach (var raw in File.ReadLines(mapPath))
    {
        var line = StripComment(raw);
        if (line.Trim().Length == 0) continue;
        if (!char.IsWhiteSpace(line[0])) { inAreas = line.StartsWith("areas:", StringComparison.Ordinal); inSources = false; continue; }
        if (!inAreas) continue;
        var t = line.Trim();
        var nameMatch = Regex.Match(t, @"^- name:\s*(\S+)$");
        if (nameMatch.Success) { sources = new(); areas.Add((nameMatch.Groups[1].Value, sources)); inSources = false; continue; }
        if (t.StartsWith("sources:", StringComparison.Ordinal)) { inSources = true; continue; }
        if (Regex.IsMatch(t, @"^[a-z-]+:")) { inSources = false; continue; }
        if (inSources && t.StartsWith("- ", StringComparison.Ordinal) && sources != null) { sources.Add(t[2..].Trim().Trim('"', '\'')); continue; }
        Fail($"Unexpected line under areas: in the map: '{raw.Trim()}'");
    }
    return areas;
}

static string StripComment(string line)
{
    var i = line.IndexOf(" #", StringComparison.Ordinal);
    if (line.TrimStart().StartsWith('#')) return "";
    return i >= 0 ? line[..i] : line;
}

static IEnumerable<string> GitLsFiles(string cwd, List<string> globs)
{
    var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true };
    psi.ArgumentList.Add("ls-files"); psi.ArgumentList.Add("-z"); psi.ArgumentList.Add("--");
    foreach (var g in globs) psi.ArgumentList.Add(":(glob)" + g);
    using var p = Process.Start(psi)!;
    var output = p.StandardOutput.ReadToEnd();
    var error = p.StandardError.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0) Fail($"git ls-files failed: {error.Trim()}");
    return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
}

static string? FindFramework(string from)
{
    for (var d = new DirectoryInfo(from); d != null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "docs", "specs", ".map.yml"))) return d.FullName;
        var nested = Path.Combine(d.FullName, "Birko.Framework");
        if (File.Exists(Path.Combine(nested, "docs", "specs", ".map.yml"))) return nested;
    }
    return null;
}

static T FailValue<T>(string message) { Console.Error.WriteLine($"spec-links: {message}"); Environment.Exit(2); return default!; }
static void Fail(string message) => FailValue<int>(message);

static class Marker
{
    public const string Begin = "<!-- spec-links: generated by Birko.Framework/spec-links.cs from docs/specs/.map.yml. Do not edit: change the map and run `dotnet run spec-links.cs`. -->";
    public const string End = "<!-- /spec-links -->";
}
