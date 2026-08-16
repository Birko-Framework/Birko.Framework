using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-218 — on .NET 9+ an <b>array</b>'s instance-style <c>set.Contains(x.Col)</c> binds to
/// <c>MemoryExtensions.Contains(ReadOnlySpan&lt;T&gt;, T)</c> rather than <c>Enumerable.Contains</c>.
/// MongoDB forwards the raw expression to the driver's LINQ translator, which does not know that
/// method: <c>NotSupportedException: Specified method is not supported</c>, naming no method, with a
/// working look-alike (<c>List&lt;T&gt;</c>) one keystroke away.
///
/// Measured across the backends that translate a filter expression: SQL and ElasticSearch were already
/// correct for all four spellings (they evaluate the operand themselves), so this rewrite lives here and
/// is wired only in MongoDB.
///
/// These tests are non-gated by design — the binding is a compile-time fact and the rewrite is a pure
/// tree transform, so neither needs a server.
/// </summary>
public class SpanContainsTests
{
    public class Model { public int Count { get; set; } public Status State { get; set; } }

    public enum Status { New, Active, Closed }

    [Fact]
    public void An_array_Contains_really_does_bind_MemoryExtensions()
    {
        // Pins the premise, not the fix. If a future runtime changes this binding back, the rewrite
        // becomes dead code and this test is what says so — rather than the rewrite silently never firing.
        var arr = new[] { 1, 5 };
        Expression<Func<Model, bool>> e = x => arr.Contains(x.Count);

        var call = (MethodCallExpression)e.Body;
        call.Method.DeclaringType.Should().Be(typeof(MemoryExtensions));
        call.Arguments[0].Type.Should().Be(typeof(ReadOnlySpan<int>));
    }

    [Fact]
    public void The_span_bound_Contains_is_rewritten_to_Enumerable_Contains()
    {
        var arr = new[] { 1, 5 };
        Expression<Func<Model, bool>> e = x => arr.Contains(x.Count);

        var call = (MethodCallExpression)SpanContains.Rewrite(e)!.Body;

        call.Method.DeclaringType.Should().Be(typeof(Enumerable));
        call.Method.Name.Should().Be(nameof(Enumerable.Contains));
        call.Arguments.Should().HaveCount(2);
    }

    [Fact]
    public void An_enum_set_uses_the_three_argument_overload_and_is_still_rewritten()
    {
        // T not IEquatable<T> — true of every enum — binds the IEqualityComparer overload with a
        // compiler-inserted null comparer. This is the shape that separately broke the SQL parser
        // (Symbio TASK-249/254), so it must not be the one the rewrite skips.
        var states = new[] { Status.Active, Status.Closed };
        Expression<Func<Model, bool>> e = x => states.Contains(x.State);

        ((MethodCallExpression)e.Body).Arguments.Should().HaveCount(3, "the premise: enum binds the comparer overload");

        var call = (MethodCallExpression)SpanContains.Rewrite(e)!.Body;
        call.Method.DeclaringType.Should().Be(typeof(Enumerable));
        call.Arguments.Should().HaveCount(2, "a null comparer carries no meaning to preserve");
    }

    [Fact]
    public void A_real_comparer_is_left_alone_rather_than_silently_dropped()
    {
        // Enumerable.Contains(source, item) cannot honour a comparer, and the 3-argument Enumerable
        // overload is no more translatable than the span one. Rewriting would silently change the
        // predicate's meaning; leaving it lets the driver report it. Narrow on purpose.
        var arr = new[] { 1, 5 };
        var comparer = EqualityComparer<int>.Default;
        Expression<Func<Model, bool>> e = x => MemoryExtensions.Contains(arr, x.Count, comparer);

        var rewritten = SpanContains.Rewrite(e)!;

        ((MethodCallExpression)rewritten.Body).Method.DeclaringType.Should().Be(typeof(MemoryExtensions));
    }

    [Theory]
    [MemberData(nameof(EquivalentSpellings))]
    public void Every_spelling_evaluates_identically_after_the_rewrite(string label, Expression<Func<Model, bool>> e)
    {
        // The rewrite must preserve meaning, not merely change the node. Compiled-delegate oracle over
        // a matching and a non-matching value, both directions asserted.
        var compiled = SpanContains.Rewrite(e)!.Compile();

        compiled(new Model { Count = 5 }).Should().BeTrue(label);
        compiled(new Model { Count = 9 }).Should().BeFalse(label);
    }

    public static TheoryData<string, Expression<Func<Model, bool>>> EquivalentSpellings()
    {
        var arr = new[] { 1, 5 };
        var list = new List<int> { 1, 5 };
        IEnumerable<int> seq = arr;
        return new()
        {
            { "int[]", x => arr.Contains(x.Count) },
            { "List<int>", x => list.Contains(x.Count) },
            { "IEnumerable<int>", x => seq.Contains(x.Count) },
            { "Enumerable.Contains", x => Enumerable.Contains(arr, x.Count) },
        };
    }

    [Fact]
    public void A_predicate_with_no_span_Contains_is_returned_unchanged()
    {
        // Reference-identity, not just equivalence: the rewrite must be a genuine no-op for the
        // overwhelming majority of filters, so it can sit on every read path without allocating.
        var list = new List<int> { 1, 5 };
        Expression<Func<Model, bool>> e = x => x.Count > 2 && list.Contains(x.Count);

        SpanContains.Rewrite(e).Should().BeSameAs(e);
    }

    [Fact]
    public void A_null_predicate_stays_null()
    {
        SpanContains.Rewrite<Model>(null).Should().BeNull();
    }

    [Fact]
    public void A_nested_span_Contains_is_rewritten_too()
    {
        // The rewrite is a visitor, not a body-level special case: the array Contains is commonly one
        // conjunct of a larger predicate, which is exactly how the MongoDB failure was first seen.
        var arr = new[] { 1, 5 };
        Expression<Func<Model, bool>> e = x => x.Count > 0 && !arr.Contains(x.Count);

        var rewritten = SpanContains.Rewrite(e)!;

        rewritten.ToString().Should().NotContain("MemoryExtensions");
        rewritten.Compile()(new Model { Count = 5 }).Should().BeFalse();
        rewritten.Compile()(new Model { Count = 9 }).Should().BeTrue();
    }
}
