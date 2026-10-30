using Avalonia.Headless.XUnit;
using Birko.Xaml.Avalonia.Tests.Parity;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Avalonia.Tests;

/// <summary>
/// TASK-791 (FEATURE-021 D8): every counterpart <c>PARITY.md</c> names exists, by its status's rule. Needs no web
/// checkout, so it runs everywhere.
/// </summary>
public class ParityCounterpartTests
{
    private static IReadOnlyList<string> Check(string status, string desktop) =>
        new Counterparts().Validate([new ParityRow("b-x", status, desktop, "", 1)]);

    [AvaloniaTheory]
    [InlineData("Components")]
    [InlineData("Shell")]
    public void Every_counterpart_in_the_list_exists(string section) =>
        new Counterparts().Validate(ParityList.Parse(File.ReadAllText(WebCheckout.ParityListPath()), section))
            .Should().BeEmpty();

    [AvaloniaTheory]
    [InlineData("built", "`TagInput`")]
    [InlineData("built", "`DialogService.Notify`")]
    [InlineData("built", "`ListPageViewModel` and `ListPageView`")]
    [InlineData("form-only", "`FieldType.DateRange`")]
    [InlineData("themed", "`BCard`")]
    [InlineData("themed", "`Button`")]
    [InlineData("themed", "`DataGrid`")]
    [InlineData("needs-theme", "`NumericUpDown`")]
    [InlineData("missing", "")]
    public void A_counterpart_that_exists_passes(string status, string desktop) =>
        Check(status, desktop).Should().BeEmpty();

    [AvaloniaTheory]
    [InlineData("built", "`NoSuchControl`", "is not a type or member in the Birko.Xaml assemblies")]
    [InlineData("built", "`DialogService.NoSuchMethod`", "is not a type or member in the Birko.Xaml assemblies")]
    [InlineData("built", "`Button`", "is not a type or member in the Birko.Xaml assemblies")]
    [InlineData("form-only", "`TagInput`", "is not a FieldType member")]
    [InlineData("form-only", "`FieldType.NoSuchField`", "is not a FieldType member")]
    [InlineData("themed", "`BNoSuchTheme`", "is neither a Birko theme key nor an Avalonia control the Birko theme styles")]
    [InlineData("themed", "`NumericUpDown`", "is neither a Birko theme key nor an Avalonia control the Birko theme styles")]
    [InlineData("needs-theme", "`NoSuchControl`", "is not an Avalonia control")]
    [InlineData("needs-theme", "`Button`", "is already styled by the Birko theme")]
    public void A_counterpart_that_does_not_hold_is_named(string status, string desktop, string why) =>
        Check(status, desktop).Should().ContainSingle().Which.Should().Contain(why);

    [AvaloniaTheory]
    [InlineData("built")]
    [InlineData("themed")]
    [InlineData("form-only")]
    public void A_ported_row_must_name_a_counterpart(string status) =>
        Check(status, "something in plain words").Should().ContainSingle().Which.Should().Contain("names no counterpart");
}
