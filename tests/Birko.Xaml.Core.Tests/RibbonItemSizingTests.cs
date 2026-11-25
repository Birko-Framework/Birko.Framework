using Birko.Xaml.Core.Ribbon;
using FluentAssertions;
using Xunit;
using S = Birko.Xaml.Core.Ribbon.RibbonGroupSize;

namespace Birko.Xaml.Core.Tests;

/// <summary>
/// TASK-120 (FEATURE-015 D13 / D14): the size each item renders at, for the step its group is at. The same cases are
/// asserted for b-ribbon's <c>ribbonItemSizes</c> in the Playground's <c>ribbon-scaling-smoke</c>, case for case, so
/// the two skins cannot drift.
/// </summary>
public class RibbonItemSizingTests
{
    private static RibbonItem Item(string id, S? size = null) => new() { Id = id, Label = id, Size = size };

    /// <summary>Word's Clipboard: a big Paste beside Cut, Copy and Format Painter.</summary>
    private static RibbonGroup Clipboard(IReadOnlyDictionary<S, IReadOnlyDictionary<string, S>>? layouts = null) => new()
    {
        Label = "Clipboard",
        Items = new[] { Item("paste", S.Large), Item("cut"), Item("copy"), Item("format") },
        Layouts = layouts,
    };

    private static string Sizes(RibbonGroup g, S step, S start = S.Medium) =>
        string.Join(",", RibbonItemSizing.SizesAt(g, step, start).Select(s => s.ToString()[0]));

    [Theory]
    [InlineData(S.Large, "L,L,L")]
    [InlineData(S.Medium, "M,M,M")]
    [InlineData(S.Small, "S,S,S")]
    [InlineData(S.Popup, "L,L,L")]
    public void A_group_with_no_item_sizes_renders_every_item_at_its_step_as_before(S step, string expected)
    {
        var g = new RibbonGroup { Label = "Plain", Items = new[] { Item("a"), Item("b"), Item("c") } };

        Sizes(g, step).Should().Be(expected);
    }

    [Theory]
    [InlineData(S.Large, "L,L,L,L")]
    [InlineData(S.Medium, "L,M,M,M")]
    [InlineData(S.Small, "M,S,S,S")]
    [InlineData(S.Popup, "L,L,L,L")]
    public void An_anchored_item_keeps_its_offset_from_the_starting_step(S step, string expected)
    {
        Sizes(Clipboard(), step).Should().Be(expected, "Paste is one step bigger than the rest wherever it fits");
    }

    [Theory]
    [InlineData(S.Large, "M")]
    [InlineData(S.Medium, "S")]
    [InlineData(S.Small, "S")]
    public void A_smaller_anchor_clamps_at_Small(S step, string expected)
    {
        var g = new RibbonGroup { Label = "G", Items = new[] { Item("x", S.Small) } };

        Sizes(g, step).Should().Be(expected);
    }

    [Theory]
    [InlineData(S.Large, "L,L,L,L")]
    [InlineData(S.Medium, "L,M,M,M")]
    [InlineData(S.Small, "L,S,S,S")]
    public void A_declared_layout_overrides_the_anchor_for_the_items_it_lists(S step, string expected)
    {
        var g = Clipboard(new Dictionary<S, IReadOnlyDictionary<string, S>>
        {
            [S.Small] = new Dictionary<string, S> { ["paste"] = S.Large },
        });

        Sizes(g, step).Should().Be(expected, "Office keeps Paste Large one step longer");
    }

    [Fact]
    public void The_offset_is_from_the_starting_step()
    {
        Sizes(Clipboard(), S.Large, start: S.Large).Should().Be("L,L,L,L");
        Sizes(Clipboard(), S.Medium, start: S.Large).Should().Be("M,M,M,M", "starting Large, Paste has no offset");
    }

    [Fact]
    public void An_item_sized_Popup_is_refused()
    {
        var g = new RibbonGroup { Label = "G", Items = new[] { Item("x", S.Popup) } };

        var act = () => RibbonItemSizing.SizesAt(g, S.Medium, S.Medium);

        act.Should().Throw<ArgumentException>().WithMessage("*'x'*cannot be Popup*");
    }

    [Fact]
    public void A_layout_for_the_Popup_step_is_refused()
    {
        var g = Clipboard(new Dictionary<S, IReadOnlyDictionary<string, S>> { [S.Popup] = new Dictionary<string, S> { ["paste"] = S.Large } });

        var act = () => RibbonItemSizing.SizesAt(g, S.Medium, S.Medium);

        act.Should().Throw<ArgumentException>().WithMessage("*layout for Popup*");
    }

    [Fact]
    public void A_layout_naming_an_unknown_item_is_refused()
    {
        var g = Clipboard(new Dictionary<S, IReadOnlyDictionary<string, S>> { [S.Small] = new Dictionary<string, S> { ["pastee"] = S.Large } });

        var act = () => RibbonItemSizing.SizesAt(g, S.Small, S.Medium);

        act.Should().Throw<ArgumentException>().WithMessage("*'pastee'*not one of its items*");
    }

    [Fact]
    public void A_layout_size_of_Popup_is_refused()
    {
        var g = Clipboard(new Dictionary<S, IReadOnlyDictionary<string, S>> { [S.Small] = new Dictionary<string, S> { ["paste"] = S.Popup } });

        var act = () => RibbonItemSizing.SizesAt(g, S.Small, S.Medium);

        act.Should().Throw<ArgumentException>().WithMessage("*'paste' as Popup*");
    }

    [Fact]
    public void A_Popup_starting_step_is_refused()
    {
        var act = () => RibbonItemSizing.SizesAt(Clipboard(), S.Medium, S.Popup);

        act.Should().Throw<ArgumentException>().WithMessage("*starting step cannot be Popup*");
    }
}
