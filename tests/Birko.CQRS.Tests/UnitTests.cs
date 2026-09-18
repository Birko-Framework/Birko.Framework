using Birko.CQRS;
using FluentAssertions;
using Xunit;

namespace Birko.CQRS.Tests;

public class UnitTests
{
    [Fact]
    public void Value_ReturnsDefault()
    {
        var unit = Unit.Value;
        unit.Should().Be(default(Unit));
    }

    [Fact]
    public async Task Task_ReturnsCompletedTask()
    {
        var task = Unit.Task;
        task.IsCompleted.Should().BeTrue();
        var result = await task;
        result.Should().Be(Unit.Value);
    }

    [Fact]
    public void Equals_AlwaysTrue()
    {
        var a = Unit.Value;
        var b = new Unit();
        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    [Fact]
    public void Equals_Object_TrueForUnit()
    {
        object obj = Unit.Value;
        Unit.Value.Equals(obj).Should().BeTrue();
    }

    [Fact]
    public void Equals_Object_FalseForNonUnit()
    {
        Unit.Value.Equals("not a unit").Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_AlwaysZero()
    {
        Unit.Value.GetHashCode().Should().Be(0);
    }

    [Fact]
    public void CompareTo_AlwaysZero()
    {
        Unit.Value.CompareTo(new Unit()).Should().Be(0);
    }

    [Fact]
    public void ToString_ReturnsBrackets()
    {
        Unit.Value.ToString().Should().Be("()");
    }
}
