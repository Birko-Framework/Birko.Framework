using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Birko.CQRS.Tests;

/// <summary>
/// Every test class that touches <c>CreateItemHandler.WasHandled</c> is in the collection that
/// serialises them.
/// </summary>
/// <remarks>
/// <para>
/// The flag is process-wide — the mediator resolves its handler from DI, so a test cannot hold the
/// instance — and xUnit runs test classes in parallel. CI saw
/// <c>Pipeline_ShortCircuit_DoesNotCallHandler</c> fail on 2026-09-18 ("Expected ... to be False, but
/// found True") while three consecutive local runs were green.
/// </para>
/// <para>
/// This exists because <b>nothing about the symptom points at the cause</b>: the class that fails is
/// the innocent one, so a future author debugging it has no path back to the class that wrote the
/// flag. The collection fixes today's pair; this fails the moment a third class joins them without
/// saying so, which is the only thing that stops the flake coming back wearing a different name.
/// </para>
/// </remarks>
public class SharedHandlerStateIsolationTests
{
    /// <summary>The member whose sharing is the hazard, assembled so this file does not match itself.</summary>
    private const string SharedMember = "CreateItemHandler" + "." + "WasHandled";

    private static string TestProjectDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "")
        => Path.GetDirectoryName(path)!;

    [Fact]
    public void Every_class_touching_the_shared_flag_declares_the_collection()
    {
        var offenders = Directory
            .EnumerateFiles(TestProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !string.Equals(Path.GetFileName(f), nameof(SharedHandlerStateIsolationTests) + ".cs", StringComparison.Ordinal))
            .Select(f => (File: Path.GetFileName(f), Text: File.ReadAllText(f)))
            // Comments explain the hazard by name on purpose, so scan code only — otherwise the
            // explanation trips the guard (§ TASK-276: a guard that must name what it forbids has to
            // avoid matching itself, and excluding its own file is not enough).
            .Select(x => (x.File, Code: StripComments(x.Text)))
            .Where(x => x.Code.Contains(SharedMember, StringComparison.Ordinal))
            .Where(x => !x.Code.Contains("[Collection(CreateItemHandlerStateCollection.Name)]", StringComparison.Ordinal))
            .Select(x => x.File)
            .ToList();

        offenders.Should().BeEmpty(
            "a class writing {0} must be serialised against the others or it fails one of them at random; "
            + "add [Collection(CreateItemHandlerStateCollection.Name)]", SharedMember);
    }

    [Fact]
    public void The_collection_still_covers_more_than_one_class()
    {
        // If this ever drops to one, the collection is doing nothing and should be deleted rather
        // than left as a rule nobody can violate — a ledger that keeps a stale entry stops being a
        // record and becomes a blanket.
        var users = Directory
            .EnumerateFiles(TestProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Select(StripComments)
            .Count(code => code.Contains("[Collection(CreateItemHandlerStateCollection.Name)]", StringComparison.Ordinal));

        users.Should().BeGreaterThanOrEqualTo(2);
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join(
            "\n",
            source.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }
}
