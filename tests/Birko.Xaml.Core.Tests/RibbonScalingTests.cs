using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// STORY-049 / TASK-099: the progressive-scaling policy. Renderer-free, so the *policy* is testable
/// without a window — which is the reason it lives in Core rather than being written twice.
/// </summary>
public class RibbonScalingTests
{
    /// <summary>A group whose variants cost 100 / 60 / 30 / 10 — roughly the real ratios.</summary>
    private static RibbonGroupMetrics G(
        int priority = 0,
        RibbonGroupSize min = RibbonGroupSize.Popup,
        double large = 100, double medium = 60, double small = 30, double popup = 10) =>
        new()
        {
            ScalingPriority = priority,
            MinSize = min,
            Widths = new Dictionary<RibbonGroupSize, double>
            {
                [RibbonGroupSize.Large] = large,
                [RibbonGroupSize.Medium] = medium,
                [RibbonGroupSize.Small] = small,
                [RibbonGroupSize.Popup] = popup,
            },
        };

    [Fact]
    public void Everything_stays_at_the_preferred_variant_when_it_fits()
    {
        var result = RibbonScaling.Resolve(new[] { G(), G(), G() }, available: 1000, preferred: RibbonGroupSize.Large);
        result.Should().AllBeEquivalentTo(RibbonGroupSize.Large);
    }

    [Fact]
    public void Medium_is_the_default_preferred_variant()
    {
        // Deliberate: both skins shipped a Medium-ish row before this pass, so the default keeps an
        // existing consumer's ribbon the same height. Large is opt-in.
        var result = RibbonScaling.Resolve(new[] { G() }, available: 1000);
        result[0].Should().Be(RibbonGroupSize.Medium);
    }

    [Fact]
    public void The_least_important_group_degrades_first()
    {
        // Two groups at Large = 200. Room for 160, so exactly one step is needed.
        var hero = G(priority: 10);
        var chrome = G(priority: 0);

        var result = RibbonScaling.Resolve(new[] { hero, chrome }, available: 160, preferred: RibbonGroupSize.Large);

        result[0].Should().Be(RibbonGroupSize.Large, "the hero group keeps its big icons longest");
        result[1].Should().Be(RibbonGroupSize.Medium, "the incidental group gives up room first");
    }

    [Fact]
    public void A_low_priority_group_bottoms_out_before_a_high_priority_one_gives_anything()
    {
        // This is the acceptance criterion spelled out: uniform shrinking would put both at Medium.
        var hero = G(priority: 10);
        var chrome = G(priority: 0);

        var result = RibbonScaling.Resolve(new[] { hero, chrome }, available: 135, preferred: RibbonGroupSize.Large);

        result[1].Should().Be(RibbonGroupSize.Small, "it degraded all the way before the hero was touched");
        result[0].Should().Be(RibbonGroupSize.Large);
    }

    [Fact]
    public void A_floor_is_honoured_even_when_it_forces_another_group_lower()
    {
        var protectedGroup = G(priority: 0, min: RibbonGroupSize.Medium); // may not go below Medium
        var other = G(priority: 5);

        var result = RibbonScaling.Resolve(new[] { protectedGroup, other }, available: 95, preferred: RibbonGroupSize.Large);

        result[0].Should().Be(RibbonGroupSize.Medium, "its floor stops it, despite being least important");
        result[1].Should().Be(RibbonGroupSize.Small, "so the cost lands on the group that still can give");
    }

    [Fact]
    public void A_floor_tighter_than_preferred_does_not_make_a_group_roomier()
    {
        // MinSize is a limit, not a target: it must never promote a group past the ribbon's look.
        var result = RibbonScaling.Resolve(
            new[] { G(min: RibbonGroupSize.Popup) }, available: 10_000, preferred: RibbonGroupSize.Small);
        result[0].Should().Be(RibbonGroupSize.Small);
    }

    [Fact]
    public void Widening_promotes_back_up()
    {
        var groups = new[] { G(), G() };
        RibbonScaling.Resolve(groups, available: 100, preferred: RibbonGroupSize.Large)
            .Should().NotContain(RibbonGroupSize.Large);
        RibbonScaling.Resolve(groups, available: 1000, preferred: RibbonGroupSize.Large)
            .Should().AllBeEquivalentTo(RibbonGroupSize.Large);
    }

    [Fact]
    public void The_same_width_always_yields_the_same_variants_whichever_way_it_was_reached()
    {
        // The stability criterion. A pass that fed the applied layout back in would answer differently
        // depending on the direction of travel, which is exactly what reads as flicker.
        var groups = new[] { G(priority: 2), G(priority: 1), G(priority: 0) };

        var descending = new List<RibbonGroupSize[]>();
        for (double w = 320; w >= 60; w -= 10) descending.Add(RibbonScaling.Resolve(groups, w, RibbonGroupSize.Large));

        var ascending = new List<RibbonGroupSize[]>();
        for (double w = 60; w <= 320; w += 10) ascending.Add(RibbonScaling.Resolve(groups, w, RibbonGroupSize.Large));
        ascending.Reverse();

        descending.Should().BeEquivalentTo(ascending, "the result depends only on the width, not the history");
    }

    [Fact]
    public void It_gives_up_rather_than_looping_when_nothing_can_degrade_further()
    {
        // Below the narrowest possible row the caller has to clip; the pass must not spin.
        var result = RibbonScaling.Resolve(new[] { G(), G() }, available: 1, preferred: RibbonGroupSize.Large);
        result.Should().AllBeEquivalentTo(RibbonGroupSize.Popup);
    }

    [Fact]
    public void Gaps_between_groups_count_towards_the_budget()
    {
        var groups = new[] { G(), G() };
        // 2 × Medium = 120 fits 130 with no gap, but not with a 20px gap.
        RibbonScaling.Resolve(groups, 130, RibbonGroupSize.Medium, gap: 0)
            .Should().AllBeEquivalentTo(RibbonGroupSize.Medium);
        RibbonScaling.Resolve(groups, 130, RibbonGroupSize.Medium, gap: 20)
            .Should().Contain(RibbonGroupSize.Small);
    }

    [Fact]
    public void An_unmeasured_variant_is_costed_as_the_nearest_roomier_one()
    {
        // A renderer that measured only Large must not make Small look free — that would let the row
        // "fit" by accident and clip commands, the very defect this story exists to remove.
        var partial = new RibbonGroupMetrics
        {
            Widths = new Dictionary<RibbonGroupSize, double> { [RibbonGroupSize.Large] = 100 },
        };

        var result = RibbonScaling.Resolve(new[] { partial }, available: 50, preferred: RibbonGroupSize.Large);
        result[0].Should().Be(RibbonGroupSize.Popup, "it degraded as far as it could and still did not fit");
    }

    [Fact]
    public void An_empty_row_resolves_to_nothing_without_throwing()
    {
        RibbonScaling.Resolve(System.Array.Empty<RibbonGroupMetrics>(), available: 100).Should().BeEmpty();
    }
}
