using Birko.Validation;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Validation.Tests.Core;

public class ValidationContextTests
{
    [Fact]
    public void Constructor_NullInstance_Throws()
    {
        var act = () => new ValidationContext(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("instance");
    }

    [Fact]
    public void Constructor_SetsInstanceAndType()
    {
        var obj = "test";
        var context = new ValidationContext(obj);

        context.Instance.Should().BeSameAs(obj);
        context.InstanceType.Should().Be(typeof(string));
    }

    [Fact]
    public void Items_CanAddAndRetrieve()
    {
        var context = new ValidationContext(new object());
        context.Items["key"] = "value";

        context.Items["key"].Should().Be("value");
    }

    [Fact]
    public void For_CreatesTypedContext()
    {
        var model = "test";
        var context = ValidationContext.For(model);

        context.Should().BeAssignableTo<ValidationContext>();
        context.Instance.Should().BeSameAs(model);
    }

    [Fact]
    public void TypedContext_InstanceProperty_ReturnsTyped()
    {
        var model = "hello";
        var context = new ValidationContext<string>(model);

        context.Instance.Should().Be("hello");
        ((ValidationContext)context).Instance.Should().BeSameAs(model);
    }
}
