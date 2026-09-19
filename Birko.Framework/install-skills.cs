#!/usr/bin/env dotnet
#:project tools/AuditCommon/AuditCommon.csproj

// install-skills — installs the SHAREABLE Birko skills into ~/.claude/skills as links pointing back
// into this repo's .claude/skills, so the repo stays the single source of truth and `git pull`
// updates the live skills with no re-install.
//
// Run:  dotnet run install-skills.cs      (idempotent; safe to re-run)
//
// Only the consumer-facing skills are shared user-level (they're needed OUTSIDE this repo —
// scaffolding a new consumer, prototyping in a consumer app). The rest of .claude/skills
// (new-birko-subproject, new-store-backend, verify-birko-conventions, roll-birko-changelog)
// stay project-local, which is exactly their scope.
//
// !! NAME-SHADOWING DOES NOT WORK — do not reintroduce it (TASK-267).
// This header used to claim verify-conventions and roll-changelog "deliberately share the
// generic skills' names so they SHADOW them here", and that renaming either one would
// "silently disarm the gates". Both statements are false, and backwards. Measured
// 2026-09-07 from the skill loader's own banner: a name present at BOTH ~/.claude/skills
// and this repo's .claude/skills resolves USER-LEVEL FIRST, so the colliding local copies
// never ran and every close gate silently linted with the generic skill. A distinct name
// is what ARMS them: the generic verify-conventions now discovers
// .claude/skills/verify-birko-conventions/ by path and hands off to it, and reports a
// blocker if it finds one it did not run.
//
// !! Still NEVER add these two to Shared. Linking a project-local variant into ~/.claude/skills
// would apply the Birko-specific checks to EVERY project on this machine. Same hazard the
// lifecycle repo's skills-pi/ carries.
//
// These skills BUILD ON TOP of the generic project-lifecycle-skills set
// (github.com -> project-lifecycle-skills; install that one first) — e.g.
// birko-new-project hands off to the generic new-project for the universal layer.
//
// PORTED FROM PowerShell 2026-09-19 (TASK-476). This is the one script of the four whose problem
// was genuinely OS-shaped rather than a path separator: `New-Item -ItemType Junction` is
// Windows-only. See LinkDirectory below for why the two platforms take different routes and why
// Windows does NOT simply use the portable API.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using AuditCommon;

static string ScriptDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

string[] shared = ["birko-new-project", "new-birko-web-page", "new-birko-web-component", "design-agent"];

var repoSkills = Path.Combine(ScriptDirectory(), ".claude", "skills");
var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
if (string.IsNullOrWhiteSpace(home))
    throw new InvalidOperationException("Could not determine the user profile directory.");

var target = Path.Combine(home, ".claude", "skills");
Directory.CreateDirectory(target);

var failed = false;

foreach (var name in shared)
{
    var source = Path.Combine(repoSkills, name);
    if (!Directory.Exists(source))
    {
        Report.Warn($"{name}: missing in {repoSkills} — skipped");
        failed = true;
        continue;
    }

    var link = Path.Combine(target, name);

    var existing = new DirectoryInfo(link);

    // `Exists` is false for a link whose target has gone away, but the reparse point is still on disk
    // and mklink/CreateSymbolicLink will refuse to overwrite it. Checking LinkTarget as well means a
    // dangling link is reported as one rather than surfacing as a confusing "file already exists".
    if (existing.Exists || File.Exists(link) || existing.LinkTarget is not null)
    {
        // LinkTarget is non-null for a symlink AND for a Windows junction (.NET 6+ resolves both
        // reparse-point kinds), so one check covers whichever route created it.
        if (existing.LinkTarget is { } existingTarget)
        {
            if (!SamePath(existingTarget, source))
            {
                Report.Warn($"{name}: links elsewhere ({existingTarget}) — remove it and re-run to relink here");
                failed = true;
            }
            else
            {
                Report.Say($"= {name} (already linked)");
            }
            continue;
        }

        Report.Warn($"{name}: a real directory already exists at {link} — move it aside and re-run");
        failed = true;
        continue;
    }

    try
    {
        LinkDirectory(link, source);
        Report.Good($"+ {name} -> {source}");
    }
    catch (Exception ex)
    {
        Report.Bad($"{name}: could not link — {ex.Message}");
        failed = true;
    }
}

Report.Say();
Report.Say("Done. Shared skills resolve from this repo via links; edit here, they're live immediately.");

// A skill that did not get linked is a skill that will not run, and the reason it did not run will
// surface much later as "the gate passed". Report it in the exit code rather than only in a line.
return failed ? 1 : 0;

/// <summary>
/// Create a directory link — a junction on Windows, a symlink elsewhere.
///
/// ⚠ Windows does NOT use Directory.CreateSymbolicLink even though it exists and is portable.
/// A directory SYMLINK on Windows requires Developer Mode or an elevated prompt; a JUNCTION
/// requires neither, which is why the PowerShell original used one and why an ordinary
/// double-click-and-run install works today. Switching to the portable call would have made this
/// script start failing for every non-elevated user — a portability fix that breaks the platform
/// it already worked on. .NET has no junction API, so Windows shells out to mklink /J.
/// </summary>
static void LinkDirectory(string link, string target)
{
    if (!OperatingSystem.IsWindows())
    {
        Directory.CreateSymbolicLink(link, target);
        return;
    }

    var info = new ProcessStartInfo("cmd.exe")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    info.ArgumentList.Add("/c");
    info.ArgumentList.Add("mklink");
    info.ArgumentList.Add("/J");
    info.ArgumentList.Add(link);
    info.ArgumentList.Add(target);

    using var process = Process.Start(info)!;
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
        throw new IOException($"mklink /J failed: {(stderr + stdout).Trim()}");
}

/// <summary>Compare two paths the way the filesystem underneath them would.</summary>
static bool SamePath(string a, string b)
{
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    return string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        comparison);
}
