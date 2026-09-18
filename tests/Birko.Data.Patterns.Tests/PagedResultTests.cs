using System;
using System.Linq;
using Birko.Data.Patterns.Paging;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// CR-M126: coverage for PagedResult's boundary math (TotalPages ceiling, HasNext/PreviousPage).
/// </summary>
public class PagedResultTests
{
    private static PagedResult<int> Page(long total, int page, int pageSize) =>
        new(Enumerable.Empty<int>().ToList(), total, page, pageSize);

    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(40, 20, 2)]
    [InlineData(41, 20, 3)]
    public void TotalPages_is_the_ceiling_of_total_over_pagesize(long total, int pageSize, int expected)
    {
        Page(total, 1, pageSize).TotalPages.Should().Be(expected);
    }

    [Fact]
    public void TotalPages_is_zero_for_non_positive_pagesize()
    {
        Page(100, 1, 0).TotalPages.Should().Be(0);
    }

    [Fact]
    public void HasNextPage_true_until_the_last_page()
    {
        Page(50, 1, 20).HasNextPage.Should().BeTrue();  // page 1 of 3
        Page(50, 2, 20).HasNextPage.Should().BeTrue();  // page 2 of 3
        Page(50, 3, 20).HasNextPage.Should().BeFalse(); // last page
    }

    [Fact]
    public void HasPreviousPage_true_after_the_first_page()
    {
        Page(50, 1, 20).HasPreviousPage.Should().BeFalse();
        Page(50, 2, 20).HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Empty_has_no_items_and_zero_total()
    {
        var empty = PagedResult<int>.Empty(page: 1, pageSize: 20);

        empty.Items.Should().BeEmpty();
        empty.TotalCount.Should().Be(0);
        empty.TotalPages.Should().Be(0);
        empty.HasNextPage.Should().BeFalse();
    }
}
