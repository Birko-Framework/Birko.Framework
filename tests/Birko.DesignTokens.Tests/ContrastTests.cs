using Birko.DesignTokens;
using FluentAssertions;
using Xunit;

namespace Birko.DesignTokens.Tests;

/// <summary>
/// WCAG 2.1 contrast of every text pair a component renders, in every shipped theme, from tokens.json (TASK-130).
/// `--b-text-muted` sat at 2.45–3.75:1 for its whole life until Presenter's axe audit (TASK-562), and `inverse` put
/// white text on pale tints at 1.10:1, which no single-file lint could see: the failure exists only in the composition.
/// The scan itself is <see cref="Contrast"/>, shared with the `contrast` CLI verb; this suite is what runs it.
/// </summary>
public class ContrastTests
{
    private static readonly TokenSet Tokens = TokenSet.Load(Paths.Resolve(Array.Empty<string>()).TokensJson);

    [Fact]
    public void Every_shipped_pair_meets_its_floor_or_is_a_recorded_exception()
    {
        var report = Contrast.Evaluate(Contrast.Measure(Tokens), Contrast.Exceptions);

        report.Clean.Should().BeTrue(report.Describe());
    }

    [Fact]
    public void Every_page_theme_and_inverse_over_each_is_measured()
    {
        Contrast.Compositions(Tokens).Select(c => c.Name).Should().BeEquivalentTo(
            "light", "dark", "neon", "finstat", "inverse/light", "inverse/dark", "inverse/neon", "inverse/finstat");
    }

    [Fact]
    public void Ratio_matches_the_WCAG_reference_values()
    {
        Contrast.Ratio("#000000", "#ffffff").Should().BeApproximately(21.0, 0.01);
        Contrast.Ratio("#64748b", "#1e293b").Should().BeApproximately(3.07, 0.01);
    }

    // ── Resolution follows the cascade ──

    [Fact]
    public void A_partial_theme_inherits_what_it_does_not_declare()
    {
        var c = Compose(root: new() { ["--fg"] = "#111111" }, page: new(), inverse: new() { ["--bg"] = "#222222" });

        Contrast.Resolve(c, "--fg").Should().Be("#111111");
        Contrast.Resolve(c, "--bg").Should().Be("#222222");
    }

    [Fact]
    public void An_inherited_var_resolves_where_it_is_declared_not_in_the_partial_theme()
    {
        // :root { --a: var(--b); --b: black }  [inverse] { --b: white }  — --a was computed at :root, so it stays black.
        var c = Compose(
            root: new() { ["--a"] = "var(--b)", ["--b"] = "#000000" },
            page: new(),
            inverse: new() { ["--b"] = "#ffffff", ["--c"] = "var(--b)" });

        Contrast.Resolve(c, "--a").Should().Be("#000000");
        Contrast.Resolve(c, "--c").Should().Be("#ffffff");
    }

    [Fact]
    public void A_page_theme_wins_over_root_and_its_vars_see_both()
    {
        var c = Compose(root: new() { ["--a"] = "var(--b)", ["--b"] = "#000000" }, page: new() { ["--b"] = "#ffffff" }, inverse: null);

        Contrast.Resolve(c, "--a").Should().Be("#ffffff");
    }

    [Fact]
    public void An_undefined_token_does_not_resolve()
    {
        var c = Compose(root: new() { ["--b-bg"] = "#ffffff" }, page: new(), inverse: null);

        FluentActions.Invoking(() => Contrast.Resolve(c, "--b-missing")).Should().Throw<InvalidOperationException>();
    }

    // ── The report ──

    [Fact]
    public void A_failure_carries_its_ratio_and_both_colours()
    {
        var report = Contrast.Evaluate(new[] { Measured("#777777", "#888888") }, Array.Empty<ContrastException>());

        report.Failures.Should().ContainSingle();
        report.Describe().Should().Contain("#777777").And.Contain("#888888").And.Contain("1.").And.Contain("needs 4.5");
    }

    [Fact]
    public void Every_failure_is_reported_in_one_run()
    {
        var measured = new[] { Measured("#777777", "#888888", "x"), Measured("#777777", "#888888", "y"), Measured("#000000", "#ffffff", "z") };

        Contrast.Evaluate(measured, Array.Empty<ContrastException>()).Failures.Select(f => f.Composition)
            .Should().Equal("x", "y");
    }

    [Fact]
    public void A_recorded_exception_is_accepted_at_its_ratio_and_still_reported()
    {
        var m = Measured("#767676", "#999999");
        var report = Contrast.Evaluate(new[] { m }, new[] { Exception(Math.Round(m.Ratio, 2)) });

        report.Clean.Should().BeTrue();
        report.Accepted.Should().ContainSingle();
        report.Describe().Should().Contain("#767676");
    }

    [Fact]
    public void A_recorded_exception_that_gets_worse_fails()
    {
        var m = Measured("#777777", "#888888");
        var report = Contrast.Evaluate(new[] { m }, new[] { Exception(Math.Round(m.Ratio, 2) + 0.2) });

        report.Clean.Should().BeFalse();
        report.Worsened.Should().ContainSingle();
    }

    [Fact]
    public void A_recorded_exception_that_now_passes_fails_until_it_is_removed()
    {
        var report = Contrast.Evaluate(new[] { Measured("#000000", "#ffffff") }, new[] { Exception(2.0) });

        report.Clean.Should().BeFalse();
        report.Stale.Should().ContainSingle();
    }

    // ── helpers ──

    private static readonly ContrastPair Pair = new(PairKind.SurfaceText, "--fg", "--bg", "test");

    private static Measurement Measured(string fg, string bg, string composition = "t") =>
        new(composition, Pair, fg, bg, Contrast.Ratio(fg, bg), 4.5);

    private static ContrastException Exception(double recorded) => new("t", "--fg", "--bg", recorded, "test");

    private static Composition Compose(Dictionary<string, string> root, Dictionary<string, string> page,
        Dictionary<string, string>? inverse)
    {
        var levels = new List<IReadOnlyList<Sheet>>();
        if (inverse is not null) levels.Add(new[] { Sheet(inverse) });
        levels.Add(new[] { Sheet(page), Sheet(root) });
        return new Composition("test", levels);
    }

    private static Sheet Sheet(Dictionary<string, string> vars) => new()
    {
        Body = vars.Select(kv => new Node { Kind = "var", Name = kv.Key, Value = kv.Value }).ToList(),
    };
}
