using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores;

/// <summary>
/// CR-L206: the shared aggregation/update helpers lacked direct coverage. These pin AggregateMath's
/// numeric/empty/all-null edge cases, TruncateToBucket, TimeIntervalParser parsing, and PropertyUpdate.ApplyTo.
/// </summary>
public class SharedHelperTests
{
    // ---- AggregateMath ----

    [Fact]
    public void ComputeSum_EmptyOrAllNull_ReturnsNullOrZeroPerSemantics()
    {
        AggregateMath.ComputeSum(new List<object?>(), typeof(int)).Should().BeNull();
        // ComputeAggregate strips nulls first, so an all-null group sums an empty list.
        AggregateMath.ComputeAggregate(AggregateFunction.Sum, new List<object?> { null, null }, typeof(int), 2)
            .Should().BeNull();
    }

    [Fact]
    public void ComputeSum_And_Avg_Decimal()
    {
        AggregateMath.ComputeSum(new List<object?> { 1.5m, 2.5m }, typeof(decimal)).Should().Be(4.0m);
        AggregateMath.ComputeAvg(new List<object?> { 2m, 4m }, typeof(decimal)).Should().Be(3.0m);
    }

    [Fact]
    public void ComputeAggregate_Count_IgnoresValues_UsesGroupCount()
    {
        AggregateMath.ComputeAggregate(AggregateFunction.Count, new List<object?> { null }, typeof(int), 7)
            .Should().Be(7);
    }

    [Fact]
    public void ComputeAggregate_MinMax_SkipNulls()
    {
        AggregateMath.ComputeAggregate(AggregateFunction.Min, new List<object?> { null, 3, 1, 2 }, typeof(int), 4).Should().Be(1);
        AggregateMath.ComputeAggregate(AggregateFunction.Max, new List<object?> { null, 3, 1, 2 }, typeof(int), 4).Should().Be(3);
        AggregateMath.ComputeAggregate(AggregateFunction.Min, new List<object?> { null, null }, typeof(int), 2).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TimeSpan.TicksPerHour)]
    public void TruncateToBucket_FloorsToBoundary(long _)
    {
        var dt = new DateTime(2026, 7, 14, 13, 37, 42, DateTimeKind.Utc);
        var bucket = AggregateMath.TruncateToBucket(dt, TimeSpan.TicksPerHour);
        bucket.Should().Be(new DateTime(2026, 7, 14, 13, 0, 0, DateTimeKind.Utc));
    }

    // ---- TimeIntervalParser ----

    [Theory]
    [InlineData("5 minutes", 300)]
    [InlineData("1 hour", 3600)]
    [InlineData("2 d", 172800)]
    [InlineData("00:00:30", 30)]
    [InlineData("", 0)]
    [InlineData("garbage", 0)]
    public void Parse_HumanAndTimeSpanForms(string input, double expectedSeconds)
    {
        TimeIntervalParser.Parse(input).TotalSeconds.Should().Be(expectedSeconds);
    }

    // ---- PropertyUpdate ----

    private class Widget : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    [Fact]
    public void PropertyUpdate_ApplyTo_SetsEachAssignedProperty()
    {
        var update = new PropertyUpdate<Widget>()
            .Set(w => w.Name, "renamed")
            .Set(w => w.Amount, 99);

        var target = new Widget { Name = "old", Amount = 1 };
        update.ApplyTo(target);

        target.Name.Should().Be("renamed");
        target.Amount.Should().Be(99);
    }
}
