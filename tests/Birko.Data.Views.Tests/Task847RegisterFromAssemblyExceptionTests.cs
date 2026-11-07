using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Views.Tests;

/// <summary>
/// TASK-847. Measured before the fix: <c>RegisterFromAssembly</c> created each mapping with
/// <c>Activator.CreateInstance</c> and called its <c>Configure</c> and the builder's <c>Build</c> through
/// <c>MethodInfo.Invoke</c>, so whatever they threw reached the caller as a <c>TargetInvocationException</c> naming
/// nothing. As in TASK-846 (the user's decision), it arrives as an <c>InvalidOperationException</c> naming the mapping
/// and the stage, with the original as its inner exception.
/// </summary>
public class Task847RegisterFromAssemblyExceptionTests
{
    public class T847View
    {
        public string Name { get; set; } = string.Empty;
    }

    public enum Fault { None, Constructor, Configure, Build }

    // This assembly is scanned by other tests running in parallel, so the mapping misbehaves only on the flow that asked.
    private static readonly AsyncLocal<Fault> Inject = new AsyncLocal<Fault>();

    public class ThrowingViewMapping : IViewMapping<T847View>
    {
        public ThrowingViewMapping()
        {
            if (Inject.Value == Fault.Constructor) ThrowsHere("bad constructor");
        }

        public void Configure(ViewDefinitionBuilder<T847View> builder)
        {
            if (Inject.Value == Fault.Configure) ThrowsHere("bad configure");
            if (Inject.Value == Fault.Build) return; // no From<TSource>(), so Build refuses
            builder.From<Customer>().Select<Customer, string>(c => c.Name, v => v.Name);
        }
    }

    private static Action Scan(ViewMapRegistry registry, Fault fault) => () =>
    {
        Inject.Value = fault;
        try
        {
            registry.RegisterFromAssembly(typeof(ThrowingViewMapping).Assembly);
        }
        finally
        {
            Inject.Value = Fault.None;
        }
    };

    [Theory]
    [InlineData(Fault.Constructor, "its constructor", "bad constructor")]
    [InlineData(Fault.Configure, "Configure", "bad configure")]
    public void A_mapping_exception_is_named_by_mapping_with_its_type_message_and_stack_inside(
        Fault fault, string stage, string message)
    {
        var registry = new ViewMapRegistry();

        var thrown = Scan(registry, fault).Should().Throw<InvalidOperationException>().Which;

        thrown.Should().NotBeAssignableTo<TargetInvocationException>();
        thrown.Message.Should().Contain(typeof(ThrowingViewMapping).FullName!).And.Contain(stage).And.Contain(message);
        var inner = thrown.InnerException.Should().BeOfType<ArgumentException>().Which;
        inner.StackTrace.Should().Contain(nameof(ThrowsHere), "the original throw site is kept");
        registry.HasDefinition<T847View>().Should().BeFalse("a mapping that threw registers nothing");
    }

    [Fact]
    public void A_build_refusal_names_the_mapping_and_keeps_the_original_inside()
    {
        var registry = new ViewMapRegistry();

        var thrown = Scan(registry, Fault.Build).Should().Throw<InvalidOperationException>().Which;

        thrown.Message.Should().Contain(typeof(ThrowingViewMapping).FullName!).And.Contain("Build").And.Contain("From<TSource>()");
        thrown.InnerException.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("From<TSource>()");
        registry.HasDefinition<T847View>().Should().BeFalse();
    }

    [Fact]
    public void Without_a_throw_the_scan_registers_the_mapping()
    {
        var registry = new ViewMapRegistry();

        registry.RegisterFromAssembly(typeof(ThrowingViewMapping).Assembly);

        registry.HasDefinition<T847View>().Should().BeTrue();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowsHere(string message) => throw new ArgumentException(message, "mapping");
}
