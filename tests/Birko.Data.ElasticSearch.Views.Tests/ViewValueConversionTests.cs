using System;
using Birko.Data.ElasticSearch.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.ElasticSearch.Views.Tests;

/// <summary>
/// CR-L119: SetPropertyValue used Convert.ChangeType, which cannot convert enum or Guid targets, so those
/// group-by/aggregate columns were silently dropped. ConvertValue now handles them explicitly. These tests
/// pin the enum/Guid handling and that a genuinely incompatible value is reported as a failed conversion
/// (leaving the property at its default) rather than throwing.
/// </summary>
public class ViewValueConversionTests
{
    private enum Colour { Red, Green, Blue }

    [Fact]
    public void Enum_target_from_member_name_string_converts()
    {
        ElasticSearchViewStore<object>.ConvertValue(typeof(Colour), "Green", out var converted)
            .Should().BeTrue();
        converted.Should().Be(Colour.Green);
    }

    [Fact]
    public void Enum_target_from_numeric_underlying_value_converts()
    {
        ElasticSearchViewStore<object>.ConvertValue(typeof(Colour), 2, out var converted)
            .Should().BeTrue();
        converted.Should().Be(Colour.Blue);
    }

    [Fact]
    public void Guid_target_from_string_converts()
    {
        var id = Guid.NewGuid();
        ElasticSearchViewStore<object>.ConvertValue(typeof(Guid), id.ToString(), out var converted)
            .Should().BeTrue();
        converted.Should().Be(id);
    }

    [Fact]
    public void Guid_target_from_guid_passes_through()
    {
        var id = Guid.NewGuid();
        ElasticSearchViewStore<object>.ConvertValue(typeof(Guid), id, out var converted)
            .Should().BeTrue();
        converted.Should().Be(id);
    }

    [Fact]
    public void Numeric_target_still_converts_via_ChangeType()
    {
        ElasticSearchViewStore<object>.ConvertValue(typeof(int), "42", out var converted)
            .Should().BeTrue();
        converted.Should().Be(42);
    }

    [Fact]
    public void Incompatible_value_reports_failure_and_does_not_throw()
    {
        ElasticSearchViewStore<object>.ConvertValue(typeof(int), "not-a-number", out var converted)
            .Should().BeFalse();
        converted.Should().BeNull();
    }

    [Fact]
    public void Invalid_guid_string_reports_failure()
    {
        ElasticSearchViewStore<object>.ConvertValue(typeof(Guid), "not-a-guid", out var converted)
            .Should().BeFalse();
        converted.Should().BeNull();
    }
}

/// <summary>
/// CR-L118: ExecuteSimpleQueryAsync defaulted Size to 10000 and From to offset, so any non-zero offset with
/// the default size made From + Size exceed the ES default max_result_window (10000), which ES rejects.
/// ClampWindowSize keeps From + Size within the window.
/// </summary>
public class WindowSizeClampTests
{
    [Fact]
    public void Zero_offset_default_size_stays_at_the_window()
    {
        ElasticSearchViewStore<object>.ClampWindowSize(0, null).Should().Be(10000);
    }

    [Fact]
    public void Nonzero_offset_default_size_is_clamped_to_fit_the_window()
    {
        // From=100 + default Size=10000 would be 10100 (rejected); clamp Size to 9900.
        ElasticSearchViewStore<object>.ClampWindowSize(100, null).Should().Be(9900);
    }

    [Fact]
    public void Explicit_small_limit_within_window_is_untouched()
    {
        ElasticSearchViewStore<object>.ClampWindowSize(50, 25).Should().Be(25);
    }

    [Fact]
    public void Explicit_limit_pushing_past_the_window_is_clamped()
    {
        ElasticSearchViewStore<object>.ClampWindowSize(9995, 100).Should().Be(5);
    }

    [Fact]
    public void Offset_at_or_past_the_window_yields_zero_size()
    {
        ElasticSearchViewStore<object>.ClampWindowSize(10000, 50).Should().Be(0);
        ElasticSearchViewStore<object>.ClampWindowSize(20000, null).Should().Be(0);
    }
}
