using System;
using System.Collections.Generic;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores;

/// <summary>
/// TASK-892 — <see cref="AggregateMath.ResultType"/> names the type <see cref="AggregateMath.ComputeAggregate"/> answers in,
/// which a store that aggregates natively (SQL) converts its server's answer to. Pinned against what the in-memory
/// aggregation actually returns, so the two statements of the rule cannot drift.
/// </summary>
public class Task892AggregateResultTypeTests
{
    public static IEnumerable<object[]> Cases()
    {
        var samples = new (Type Type, object A, object B)[]
        {
            (typeof(int), 1, 2), (typeof(long), 1L, 2L), (typeof(short), (short)1, (short)2), (typeof(byte), (byte)1, (byte)2),
            (typeof(sbyte), (sbyte)1, (sbyte)2), (typeof(ushort), (ushort)1, (ushort)2), (typeof(uint), 1u, 2u),
            (typeof(ulong), 1ul, 2ul), (typeof(decimal), 1.5m, 2m), (typeof(double), 1.5, 2.0), (typeof(float), 1.5f, 2f),
        };
        foreach (var (type, a, b) in samples)
        {
            foreach (var function in new[] { AggregateFunction.Count, AggregateFunction.Sum, AggregateFunction.Avg, AggregateFunction.Min, AggregateFunction.Max })
            {
                yield return new object[] { function, type, a, b };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_result_type_is_the_type_the_in_memory_aggregation_answers_in(AggregateFunction function, Type type, object a, object b)
    {
        var computed = AggregateMath.ComputeAggregate(function, new List<object?> { a, null, b }, type, 3);

        computed.Should().NotBeNull();
        AggregateMath.ResultType(function, type).Should().Be(computed!.GetType());
        AggregateMath.ResultType(function, typeof(Nullable<>).MakeGenericType(type)).Should().Be(computed.GetType());
    }

    [Theory]
    [InlineData(AggregateFunction.Sum)]
    [InlineData(AggregateFunction.Avg)]
    public void A_sum_or_average_of_a_non_numeric_type_is_refused_as_the_in_memory_aggregation_refuses_it(AggregateFunction function)
    {
        var computed = () => AggregateMath.ComputeAggregate(function, new List<object?> { "a" }, typeof(string), 1);
        var resultType = () => AggregateMath.ResultType(function, typeof(string));

        computed.Should().Throw<NotSupportedException>();
        resultType.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void A_min_or_max_answers_the_property_type_whatever_it_is()
    {
        AggregateMath.ResultType(AggregateFunction.Min, typeof(DateTime?)).Should().Be(typeof(DateTime));
        AggregateMath.ResultType(AggregateFunction.Max, typeof(string)).Should().Be(typeof(string));
    }
}
