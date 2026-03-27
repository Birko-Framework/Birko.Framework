using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

public class ViewResultTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var items = new List<CustomerView>
        {
            new() { CustomerId = Guid.NewGuid(), CustomerName = "Alice" },
            new() { CustomerId = Guid.NewGuid(), CustomerName = "Bob" }
        };

        var result = new ViewResult<CustomerView>(items, 42);

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(42);
    }

    [Fact]
    public void Constructor_WithoutCount_LeavesNull()
    {
        var items = new List<CustomerView> { new() { CustomerName = "Alice" } };
        var result = new ViewResult<CustomerView>(items);

        result.TotalCount.Should().BeNull();
    }

    [Fact]
    public void Empty_ReturnsEmptyResult()
    {
        var result = ViewResult<CustomerView>.Empty;

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }
}
