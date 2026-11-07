using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-847. Measured before the fix: <c>RegisterFromAssembly</c> created each mapping with
/// <c>Activator.CreateInstance</c>, so an exception from the mapping's constructor reached the caller as a
/// <c>TargetInvocationException</c> naming nothing. It arrives the way TASK-846 settled for <c>Configure</c> (the user's
/// decision): an <c>InvalidOperationException</c> naming the mapping, with the original as its inner exception.
/// </summary>
public class Task847MappingConstructorExceptionTests
{
    public class T847Model
    {
        public string Code { get; set; } = string.Empty;
    }

    // This assembly is scanned by other tests running in parallel, so the constructor throws only on the flow that asked.
    private static readonly AsyncLocal<bool> ThrowInConstructor = new AsyncLocal<bool>();

    public class ThrowingConstructorMapping : IModelMapping<T847Model>
    {
        public ThrowingConstructorMapping()
        {
            if (ThrowInConstructor.Value) ThrowsHere();
        }

        public void Configure(ModelMap<T847Model> map) => map.ToTable("t847_model");
    }

    [Fact]
    public void A_constructor_exception_is_named_by_mapping_with_its_type_message_and_stack_inside()
    {
        var registry = new ModelMapRegistry();

        Action scan = () =>
        {
            ThrowInConstructor.Value = true;
            try
            {
                registry.RegisterFromAssembly(typeof(ThrowingConstructorMapping).Assembly);
            }
            finally
            {
                ThrowInConstructor.Value = false;
            }
        };

        var thrown = scan.Should().Throw<InvalidOperationException>().Which;

        thrown.Should().NotBeAssignableTo<TargetInvocationException>();
        thrown.Message.Should().Contain(typeof(ThrowingConstructorMapping).FullName!)
            .And.Contain("its constructor").And.Contain("bad constructor");
        var inner = thrown.InnerException.Should().BeOfType<ArgumentException>().Which;
        inner.StackTrace.Should().Contain(nameof(ThrowsHere), "the original throw site is kept");
        registry.HasMap<T847Model>().Should().BeFalse("a mapping that threw registers nothing");
    }

    [Fact]
    public void Without_a_throw_the_scan_registers_the_mapping()
    {
        var registry = new ModelMapRegistry();

        registry.RegisterFromAssembly(typeof(ThrowingConstructorMapping).Assembly);

        registry.GetMap<T847Model>()!.TableName.Should().Be("t847_model");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowsHere() => throw new ArgumentException("bad constructor", "mapping");
}
