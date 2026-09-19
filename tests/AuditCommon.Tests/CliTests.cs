using AuditCommon;
using FluentAssertions;
using Xunit;

namespace AuditCommonTests;

/// <summary>
/// The `[CmdletBinding()] param(...)` the PowerShell originals declared, and the two things
/// TASK-476 decided about it: both argument spellings are accepted (eight closed write-ups quote
/// the PowerShell one), and an unrecognised argument is REFUSED rather than ignored.
/// </summary>
public class CliTests
{
    [Theory]
    [InlineData("--fail-on-finding")]
    [InlineData("-FailOnFinding")]
    [InlineData("--failonfinding")]
    [InlineData("--fail_on_finding")]
    [InlineData("/FailOnFinding")]
    public void Both_the_posix_and_powershell_spellings_set_the_switch(string spelling) =>
        Cli.Parse([spelling]).FailOnFinding.Should().BeTrue();

    [Fact]
    public void The_switch_is_off_when_it_is_not_given() =>
        Cli.Parse([]).FailOnFinding.Should().BeFalse();

    [Theory]
    [InlineData("--root")]
    [InlineData("-Root")]
    public void Root_takes_the_following_argument_as_its_value(string spelling) =>
        Cli.Parse([spelling, "/some/where"]).Root.Should().Be("/some/where");

    [Fact]
    public void Root_without_a_value_is_refused_rather_than_silently_ignored()
    {
        var parse = () => Cli.Parse(["--root"]);
        parse.Should().Throw<ArgumentException>().WithMessage("*needs a path*");
    }

    [Fact]
    public void An_unrecognised_argument_is_refused()
    {
        // A typo'd `--fail-on-findings` that silently parsed as "report only" would turn a gate into
        // a no-op wearing a flag's name — the shape CLAUDE.md § Conventions keeps recording.
        var parse = () => Cli.Parse(["--fail-on-findings"]);
        parse.Should().Throw<ArgumentException>().WithMessage("*Unrecognised argument*");
    }

    [Fact]
    public void Both_options_can_be_given_together()
    {
        var cli = Cli.Parse(["--root", "/x", "--fail-on-finding"]);

        cli.Root.Should().Be("/x");
        cli.FailOnFinding.Should().BeTrue();
    }

    [Fact]
    public void ResolveRoot_walks_up_the_requested_number_of_levels()
    {
        var root = Directory.CreateTempSubdirectory("auditcommon-cli").FullName;
        try
        {
            var deep = Path.Combine(root, "Framework", "Birko.Framework");
            Directory.CreateDirectory(deep);

            Cli.Parse([]).ResolveRoot(deep, levelsUp: 2)
               .Should().Be(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveRoot_prefers_an_explicit_root_over_the_walk_up()
    {
        var root = Directory.CreateTempSubdirectory("auditcommon-cli").FullName;
        try
        {
            Cli.Parse(["--root", root]).ResolveRoot("/ignored/entirely", levelsUp: 9)
               .Should().Be(Path.GetFullPath(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveRoot_refuses_a_root_that_does_not_exist()
    {
        var absent = Path.Combine(Path.GetTempPath(), "auditcommon-absent-" + Guid.NewGuid());

        var resolve = () => Cli.Parse(["--root", absent]).ResolveRoot("/anywhere", levelsUp: 1);

        resolve.Should().Throw<DirectoryNotFoundException>();
    }
}
