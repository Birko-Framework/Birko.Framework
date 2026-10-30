using Birko.Xaml.Avalonia.Tests.Parity;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-793 (FEATURE-021): <c>Birko.Xaml.Avalonia/PARITY.md</c> has exactly one valid row per <c>b-*</c> component in
/// the neighbouring Birko.Web checkout. The fixture tests pin each rule; the live test runs them on the real list.
/// </summary>
public class ParityListTests(ITestOutputHelper output)
{
    private const string Header = "| Web component | Status | Desktop | Note |\n|---|---|---|---|\n";

    private static IReadOnlyList<string> Check(string rows, params string[] web) =>
        ParityList.Validate(ParityList.Parse("# List\n\n## Components\n\n" + Header + rows, "Components"), web);

    [Theory]
    [InlineData("Components")]
    [InlineData("Shell")]
    public void Every_web_piece_has_exactly_one_valid_row(string section)
    {
        if (WebCheckout.Locate(output.WriteLine) is not { } web) return;
        var rows = ParityList.Parse(File.ReadAllText(WebCheckout.ParityListPath()), section);
        var names = WebCheckout.Names(web, section);

        names.Should().NotBeEmpty("the web checkout was found, so it has a {0} section to cover", section);
        ParityList.Validate(rows, names).Should().BeEmpty();
    }

    [Fact]
    public void Shell_rows_are_keyed_by_path() =>
        ParityList.Validate(
                ParityList.Parse("## Shell\n\n" + Header + "| `pages/base-page` | built | `BasePageViewModel` | |\n", "Shell"),
                ["pages/base-page", "pages/base-foo-page"])
            .Should().ContainSingle().Which.Should().Be("pages/base-foo-page exists on the web but has no row");

    [Fact]
    public void A_complete_list_has_no_problems() =>
        Check("| `b-card` | themed | `BCard` | |\n| `b-tour` | skipped | | a web pattern |\n", "b-card", "b-tour")
            .Should().BeEmpty();

    [Fact]
    public void A_web_component_without_a_row_is_named() =>
        Check("| `b-card` | themed | `BCard` | |\n", "b-card", "b-foo")
            .Should().ContainSingle().Which.Should().Be("b-foo exists on the web but has no row");

    [Fact]
    public void A_row_for_a_component_gone_from_the_web_is_named() =>
        Check("| `b-card` | themed | `BCard` | |\n| `b-gone` | missing | | |\n", "b-card")
            .Should().ContainSingle().Which.Should().Contain("b-gone has a row but no longer exists on the web");

    [Fact]
    public void A_skipped_row_needs_a_reason() =>
        Check("| `b-tour` | skipped | | |\n", "b-tour")
            .Should().ContainSingle().Which.Should().Contain("b-tour is skipped with no reason");

    [Fact]
    public void An_unknown_status_is_named() =>
        Check("| `b-card` | done | `BCard` | |\n", "b-card")
            .Should().ContainSingle().Which.Should().Contain("b-card has status 'done'");

    [Fact]
    public void A_component_listed_twice_is_named() =>
        Check("| `b-card` | themed | `BCard` | |\n| `b-card` | built | `Card` | |\n", "b-card")
            .Should().ContainSingle().Which.Should().Contain("b-card has more than one row");

    [Fact]
    public void Only_the_named_section_is_read()
    {
        var markdown = "## Components\n\n" + Header + "| `b-card` | themed | | |\n\n## Shell\n\n" + Header +
                       "| `auth-store` | missing | | |\n";

        ParityList.Parse(markdown, "Components").Select(r => r.Web).Should().Equal("b-card");
        ParityList.Parse(markdown, "Shell").Select(r => r.Web).Should().Equal("auth-store");
    }

    [Fact]
    public void Rows_carry_their_line_number_and_cells()
    {
        var row = ParityList.Parse("## Components\n\n" + Header + "| `b-chart` | built | `BChart` | over LiveCharts2 |\n",
            "Components").Single();

        row.Should().Be(new ParityRow("b-chart", "built", "`BChart`", "over LiveCharts2", 5));
    }
}
