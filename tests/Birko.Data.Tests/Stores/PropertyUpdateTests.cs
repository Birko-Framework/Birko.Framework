using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores;

/// <summary>
/// TASK-498 — <see cref="PropertyUpdate{T}.Increment{TProperty}"/> and its <c>Decrement</c> alias: the builder's
/// refusals and the non-atomic <c>ApplyTo</c> fallback. What a store does with an increment is asserted per
/// provider (SQLite end-to-end, the SQL/Mongo/Elasticsearch translators).
/// <para>
/// Two refusals are compile-time and so cannot be tests: a nullable property (<c>int?</c> is not
/// <c>INumber&lt;int?&gt;</c>) and <c>Decrement</c> on an unsigned type (<c>uint</c> is not
/// <c>ISignedNumber&lt;uint&gt;</c>, whose negation would wrap to <c>uint.MaxValue</c>).
/// </para>
/// </summary>
public class PropertyUpdateTests
{
    private sealed class Item : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public int? MaybeHits { get; set; }
        public long Total { get; set; }
        public double Ratio { get; set; }
        public decimal Price { get; set; }
        public char Letter { get; set; }
        public uint Unsigned { get; set; }
        public Nested Inner { get; set; } = new();
        public int Field;
    }

    private sealed class Nested
    {
        public int Hits { get; set; }
    }

    private static object? DeltaOf(PropertyAssignment a) => a.Match<object?>(set => null, increment => increment.Delta);

    [Fact]
    public void Increment_And_Set_Chain_InOneUpdate_KeepingTheirKinds()
    {
        var update = new PropertyUpdate<Item>().Set(x => x.Name, "a").Increment(x => x.Hits, 1);

        update.Assignments.Should().HaveCount(2);
        update.Assignments[0].Should().BeOfType<SetAssignment>().Which.Value.Should().Be("a");
        update.Assignments[1].Should().BeOfType<IncrementAssignment>().Which.Delta.Should().Be(1);
    }

    [Fact]
    public void Decrement_Is_A_Negative_Increment()
    {
        var update = new PropertyUpdate<Item>().Decrement(x => x.Hits, 5).Decrement(x => x.Price, 0.5m).Decrement(x => x.Ratio, 0.25);

        update.Assignments.Should().AllBeOfType<IncrementAssignment>();
        update.Assignments.Select(DeltaOf).Should().Equal(-5, -0.5m, -0.25);
    }

    [Fact]
    public void Decrement_Of_A_Value_Without_A_Negation_Throws_Rather_Than_Wrapping()
    {
        var act = () => new PropertyUpdate<Item>().Decrement(x => x.Hits, int.MinValue);

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Increment_Through_A_Cast_Is_Refused()
    {
        // The INumber constraint refuses `x => x.MaybeHits`, but a cast hides the nullable type from it.
        var nullable = () => new PropertyUpdate<Item>().Increment(x => (int)x.MaybeHits!, 1);
        var widened = () => new PropertyUpdate<Item>().Increment(x => (long)x.Hits, 1L);

        nullable.Should().Throw<ArgumentException>().WithMessage("*direct access*");
        widened.Should().Throw<ArgumentException>().WithMessage("*direct access*");
    }

    [Fact]
    public void Increment_Of_A_Nested_Member_Is_Refused()
    {
        // Every provider resolves only the leaf, so Mongo would $inc a top-level "Hits" that does not exist.
        var act = () => new PropertyUpdate<Item>().Increment(x => x.Inner.Hits, 1);

        act.Should().Throw<ArgumentException>().WithMessage("*top-level*");
    }

    [Fact]
    public void Increment_Of_A_Type_Not_Every_Provider_Stores_Numerically_Is_Refused()
    {
        var character = () => new PropertyUpdate<Item>().Increment(x => x.Letter, (char)1);
        var unsigned = () => new PropertyUpdate<Item>().Increment(x => x.Unsigned, 1u);

        character.Should().Throw<ArgumentException>().WithMessage("*Char*");
        unsigned.Should().Throw<ArgumentException>().WithMessage("*UInt32*");
    }

    [Fact]
    public void ApplyTo_Overflow_Throws_Rather_Than_Wrapping()
    {
        var item = new Item { Hits = int.MaxValue };

        var act = () => new PropertyUpdate<Item>().Increment(x => x.Hits, 1).ApplyTo(item);

        act.Should().Throw<OverflowException>();
        item.Hits.Should().Be(int.MaxValue);
    }

    [Fact]
    public void ApplyTo_Refuses_A_Selector_That_Is_Not_A_Property()
    {
        var act = () => new PropertyUpdate<Item>().Set(x => x.Field, 1).ApplyTo(new Item());

        act.Should().Throw<ArgumentException>().WithMessage("*Unable to resolve property*");
    }

    [Fact]
    public void Increment_Combined_With_Another_Assignment_To_The_Same_Property_Is_Refused()
    {
        var setThenIncrement = () => new PropertyUpdate<Item>().Set(x => x.Hits, 3).Increment(x => x.Hits, 1);
        var incrementThenSet = () => new PropertyUpdate<Item>().Increment(x => x.Hits, 1).Set(x => x.Hits, 3);
        var incrementTwice = () => new PropertyUpdate<Item>().Increment(x => x.Hits, 1).Decrement(y => y.Hits, 1);

        setThenIncrement.Should().Throw<InvalidOperationException>().WithMessage("*Hits*");
        incrementThenSet.Should().Throw<InvalidOperationException>().WithMessage("*Hits*");
        incrementTwice.Should().Throw<InvalidOperationException>().WithMessage("*Hits*");
    }

    [Fact]
    public void Set_Twice_On_One_Property_Keeps_Todays_Behaviour()
    {
        var update = new PropertyUpdate<Item>().Set(x => x.Name, "a").Set(x => x.Name, "b");

        update.Assignments.Should().HaveCount(2);
    }

    [Fact]
    public void ApplyTo_Adds_An_Increment_Instead_Of_Assigning_It()
    {
        var item = new Item { Name = "old", Hits = 10, Total = 100, Ratio = 1.5, Price = 10.10m };

        new PropertyUpdate<Item>()
            .Set(x => x.Name, "new")
            .Increment(x => x.Hits, 5)
            .Increment(x => x.Total, 1L)
            .Increment(x => x.Ratio, 0.25)
            .Decrement(x => x.Price, 0.10m)
            .ApplyTo(item);

        item.Name.Should().Be("new");
        item.Hits.Should().Be(15);
        item.Total.Should().Be(101);
        item.Ratio.Should().Be(1.75);
        item.Price.Should().Be(10.00m);
    }
}
