using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Views;

public class ViewQueryModeTests
{
    [Fact]
    public void OnTheFly_HasValueZero()
    {
        ((int)ViewQueryMode.OnTheFly).Should().Be(0);
    }

    [Fact]
    public void Persistent_HasValueOne()
    {
        ((int)ViewQueryMode.Persistent).Should().Be(1);
    }

    [Fact]
    public void Auto_HasValueTwo()
    {
        ((int)ViewQueryMode.Auto).Should().Be(2);
    }

    [Fact]
    public void Enum_HasThreeValues()
    {
        var values = System.Enum.GetValues<ViewQueryMode>();

        values.Should().HaveCount(3);
    }
}

public class MaterializedViewTypeTests
{
    [Fact]
    public void None_HasValueZero()
    {
        ((int)MaterializedViewType.None).Should().Be(0);
    }

    [Fact]
    public void PostgreSqlMaterialized_HasValueOne()
    {
        ((int)MaterializedViewType.PostgreSqlMaterialized).Should().Be(1);
    }

    [Fact]
    public void MSSqlIndexed_HasValueTwo()
    {
        ((int)MaterializedViewType.MSSqlIndexed).Should().Be(2);
    }

    [Fact]
    public void Enum_HasThreeValues()
    {
        var values = System.Enum.GetValues<MaterializedViewType>();

        values.Should().HaveCount(3);
    }
}
