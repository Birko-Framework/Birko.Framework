using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-846. Measured before the fix: <c>RegisterFromAssembly</c> called each mapping's <c>Configure</c> through
/// <c>MethodInfo.Invoke</c>, so whatever it threw (TASK-845's <c>NotSupportedException</c>, an <c>ArgumentException</c>)
/// reached the caller as a <c>TargetInvocationException</c>: the outer message named reflection, not the mapping. It
/// arrives as an <c>InvalidOperationException</c> naming the mapping now, with the original as its inner exception (the
/// user's decision, weighed against rule 9).
/// </summary>
public class Task846RegisterFromAssemblyExceptionTests
{
    public class Thrower
    {
        public string Code { get; set; } = string.Empty;
    }

    // This assembly is scanned by other tests running in parallel, so the mapping misbehaves only on the flow that asked.
    private static readonly AsyncLocal<Action<ModelMap<Thrower>>?> Misconfigure = new AsyncLocal<Action<ModelMap<Thrower>>?>();

    public class ThrowingMapping : IModelMapping<Thrower>
    {
        public void Configure(ModelMap<Thrower> map)
        {
            Misconfigure.Value?.Invoke(map);
            map.ToTable("t846_thrower");
        }
    }

    private static Action Scan(ModelMapRegistry registry, Action<ModelMap<Thrower>> misconfigure) => () =>
    {
        Misconfigure.Value = misconfigure;
        try
        {
            registry.RegisterFromAssembly(typeof(ThrowingMapping).Assembly);
        }
        finally
        {
            Misconfigure.Value = null;
        }
    };

    [Fact]
    public void A_nested_member_refusal_names_the_mapping_and_keeps_the_original_inside()
    {
        var registry = new ModelMapRegistry();

        var thrown = Scan(registry, map => map.Property(x => x.Code.Length))
            .Should().Throw<InvalidOperationException>().Which;

        thrown.Message.Should().Contain(typeof(ThrowingMapping).FullName!).And.Contain("TASK-845");
        thrown.InnerException.Should().BeOfType<NotSupportedException>();
        registry.HasMap<Thrower>().Should().BeFalse("a mapping that threw registers nothing");
    }

    [Fact]
    public void Any_exception_is_named_by_mapping_with_its_type_message_and_stack_inside()
    {
        var registry = new ModelMapRegistry();

        var thrown = Scan(registry, _ => ThrowsHere())
            .Should().Throw<InvalidOperationException>().Which;

        thrown.Should().NotBeAssignableTo<TargetInvocationException>();
        thrown.Message.Should().Contain(typeof(ThrowingMapping).FullName!).And.Contain("bad mapping");
        var inner = thrown.InnerException.Should().BeOfType<ArgumentException>().Which;
        inner.StackTrace.Should().Contain(nameof(ThrowsHere), "the original throw site is kept");
    }

    [Fact]
    public void Without_a_throw_the_scan_registers_the_mapping()
    {
        var registry = new ModelMapRegistry();

        registry.RegisterFromAssembly(typeof(ThrowingMapping).Assembly);

        registry.GetMap<Thrower>()!.TableName.Should().Be("t846_thrower");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowsHere() => throw new ArgumentException("bad mapping", "map");
}
