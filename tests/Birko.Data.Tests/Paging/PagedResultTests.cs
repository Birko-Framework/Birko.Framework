using Birko.Data.Patterns.Paging;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Paging;

public class PagedResultTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var items = new[] { "a", "b", "c" };
        var result = new PagedResult<string>(items, 30, 2, 10);

        result.Items.Should().BeEquivalentTo(items);
        result.TotalCount.Should().Be(30);
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(10);
    }

    [Fact]
    public void TotalPages_CalculatesCorrectly()
    {
        var result = new PagedResult<int>([], 25, 1, 10);
        result.TotalPages.Should().Be(3); // ceil(25/10) = 3
    }

    [Fact]
    public void TotalPages_ExactDivision()
    {
        var result = new PagedResult<int>([], 20, 1, 10);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public void TotalPages_ZeroPageSize_ReturnsZero()
    {
        var result = new PagedResult<int>([], 10, 1, 0);
        result.TotalPages.Should().Be(0);
    }

    [Fact]
    public void HasNextPage_OnLastPage_ReturnsFalse()
    {
        var result = new PagedResult<int>([], 20, 2, 10);
        result.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public void HasNextPage_NotOnLastPage_ReturnsTrue()
    {
        var result = new PagedResult<int>([], 30, 1, 10);
        result.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public void HasPreviousPage_OnFirstPage_ReturnsFalse()
    {
        var result = new PagedResult<int>([], 20, 1, 10);
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public void HasPreviousPage_NotOnFirstPage_ReturnsTrue()
    {
        var result = new PagedResult<int>([], 20, 2, 10);
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact]
    public void Empty_ReturnsEmptyResult()
    {
        var result = PagedResult<string>.Empty();

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalPages.Should().Be(0);
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }
}
