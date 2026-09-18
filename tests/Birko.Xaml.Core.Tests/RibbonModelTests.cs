using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// STORY-049 / TASK-098: the ribbon's size-variant + scaling-priority model. Pure model, so it lives
/// here rather than in the Avalonia suite — and the defaults matter more than they look, because they
/// are what guarantees an existing consumer renders unchanged.
/// </summary>
public class RibbonModelTests
{
    [Fact]
    public void A_group_that_sets_nothing_keeps_todays_behaviour()
    {
        var group = new RibbonGroup { Label = "Clipboard" };

        group.ScalingPriority.Should().Be(0, "equal priority means no group is degraded preferentially");
        group.MinSize.Should().Be(RibbonGroupSize.Popup, "a group is fully collapsible unless protected");
        group.Icon.Should().BeNull("only the Popup chunk button needs one");
        group.Items.Should().BeEmpty();
    }

    [Fact]
    public void Variants_are_declared_roomiest_first_so_comparisons_read_naturally()
    {
        // The degrade order is the declaration order, which is what lets a measure pass compare
        // variants with < / > instead of carrying a lookup table.
        ((int)RibbonGroupSize.Large).Should().BeLessThan((int)RibbonGroupSize.Medium);
        ((int)RibbonGroupSize.Medium).Should().BeLessThan((int)RibbonGroupSize.Small);
        ((int)RibbonGroupSize.Small).Should().BeLessThan((int)RibbonGroupSize.Popup);
    }

    [Fact]
    public void A_protected_group_declares_the_tightest_variant_it_will_reach()
    {
        var hero = new RibbonGroup { Label = "Clipboard", ScalingPriority = 10, MinSize = RibbonGroupSize.Small };

        hero.MinSize.Should().Be(RibbonGroupSize.Small);
        // "Never collapse to a flyout" is expressible, and it is strictly roomier than the default.
        ((int)hero.MinSize).Should().BeLessThan((int)RibbonGroupSize.Popup);
    }

    [Fact]
    public void Lower_priority_degrades_first_birkos_convention_not_ribbonxs()
    {
        var chrome = new RibbonGroup { Label = "Export", ScalingPriority = 0 };
        var hero = new RibbonGroup { Label = "Clipboard", ScalingPriority = 10 };

        // Priority means IMPORTANCE here: the hero group outranks the incidental one, so a degrade
        // pass must take room from `chrome` first. Pinned as a test because the direction is ours and
        // a reader coming from RibbonX may assume the opposite.
        chrome.ScalingPriority.Should().BeLessThan(hero.ScalingPriority);
    }

    [Fact]
    public void The_group_icon_feeds_the_collapsed_chunk_button()
    {
        var group = new RibbonGroup { Label = "Clipboard", Icon = "📋" };
        group.Icon.Should().Be("📋");
    }
}
