using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

public class DictionaryRuleContextTests
{
    [Fact]
    public void TryGetValue_ExistingField_ReturnsTrue()
    {
        var ctx = DictionaryRuleContext.From(("Temperature", (object?)42));

        ctx.TryGetValue("Temperature", out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGetValue_MissingField_ReturnsFalse()
    {
        var ctx = DictionaryRuleContext.From(("A", (object?)1));

        ctx.TryGetValue("B", out _).Should().BeFalse();
    }

    [Fact]
    public void HasField_ExistingField_ReturnsTrue()
    {
        var ctx = DictionaryRuleContext.From(("X", (object?)null));

        ctx.HasField("X").Should().BeTrue();
    }

    [Fact]
    public void HasField_MissingField_ReturnsFalse()
    {
        var ctx = DictionaryRuleContext.From(("X", (object?)1));

        ctx.HasField("Y").Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullDictionary_Throws()
    {
        var act = () => new DictionaryRuleContext(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGetValue_NullValue_ReturnsTrueWithNull()
    {
        var ctx = DictionaryRuleContext.From(("Field", (object?)null));

        ctx.TryGetValue("Field", out var value).Should().BeTrue();
        value.Should().BeNull();
    }
}

public class ObjectRuleContextTests
{
    private class TestObject
    {
        public int Temperature { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
    }

    [Fact]
    public void TryGetValue_ExistingProperty_ReturnsTrue()
    {
        var obj = new TestObject { Temperature = 42 };
        var ctx = new ObjectRuleContext<TestObject>(obj);

        ctx.TryGetValue("Temperature", out var value).Should().BeTrue();
        value.Should().Be(42);
    }

    [Fact]
    public void TryGetValue_CaseInsensitive()
    {
        var obj = new TestObject { Name = "Test" };
        var ctx = new ObjectRuleContext<TestObject>(obj);

        ctx.TryGetValue("name", out var value).Should().BeTrue();
        value.Should().Be("Test");
    }

    [Fact]
    public void TryGetValue_MissingProperty_ReturnsFalse()
    {
        var obj = new TestObject();
        var ctx = new ObjectRuleContext<TestObject>(obj);

        ctx.TryGetValue("NonExistent", out _).Should().BeFalse();
    }

    [Fact]
    public void HasField_ExistingProperty_ReturnsTrue()
    {
        var obj = new TestObject();
        var ctx = new ObjectRuleContext<TestObject>(obj);

        ctx.HasField("Price").Should().BeTrue();
    }

    [Fact]
    public void HasField_MissingProperty_ReturnsFalse()
    {
        var obj = new TestObject();
        var ctx = new ObjectRuleContext<TestObject>(obj);

        ctx.HasField("Missing").Should().BeFalse();
    }

    [Fact]
    public void Constructor_NullInstance_Throws()
    {
        var act = () => new ObjectRuleContext<TestObject>(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
