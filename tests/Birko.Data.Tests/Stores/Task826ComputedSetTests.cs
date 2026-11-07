using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tests.Stores;

/// <summary>
/// TASK-826 — <see cref="PropertyUpdate{T}.Compute(System.Linq.Expressions.Expression{Func{T, decimal}}, System.Linq.Expressions.Expression{Func{T, decimal}})"/>:
/// what a formula may contain, what it may not be combined with, and the C# reference every provider is held to
/// (the cross-provider contract is <c>tests/Shared/StoreContract/ComputedSetContract.cs</c>).
/// </summary>
public class Task826ComputedSetTests
{
    private sealed class Price : AbstractModel
    {
        public decimal Gross { get; set; }
        public decimal Net { get; set; }
        public decimal? MaybeNet { get; set; }
        public decimal? MaybeGross { get; set; }
        public double Ratio { get; set; }
        public int Count { get; set; }
        public string? Name { get; set; }
        public Inner Child { get; set; } = new();
    }

    private sealed class Inner
    {
        public decimal Amount { get; set; }
    }

    private static decimal Apply(PropertyUpdate<Price> update, Price row, Func<Price, decimal> read)
    {
        update.ApplyTo(row);
        return read(row);
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, 0.12)]
    [InlineData(MidpointRounding.AwayFromZero, 0.13)]
    public void The_fallback_rounds_a_midpoint_with_the_mode_named(MidpointRounding mode, double expected)
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, mode));

        Apply(update, new Price { Gross = 0.15m }, p => p.Net).Should().Be((decimal)expected);
    }

    [Fact]
    public void Captured_values_and_constant_subexpressions_are_evaluated_once_when_added()
    {
        var rate = 0.2m;
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => decimal.Round(x.Gross / (1 + rate), 2, MidpointRounding.ToEven));
        rate = 0.5m; // the update keeps the value it was built with

        var computed = (ComputedAssignment)update.Assignments.Single();
        var round = (ComputedRound)computed.Value;
        var division = (ComputedBinary)round.Operand;
        ((ComputedConstant)division.Right).Value.Should().Be(1.2m);
        Apply(update, new Price { Gross = 120m }, p => p.Net).Should().Be(100m);
    }

    [Fact]
    public void Unary_minus_subtraction_multiplication_and_a_rounding_without_digits_are_supported()
    {
        var update = new PropertyUpdate<Price>()
            .Compute(x => x.Net, x => Math.Round(-x.Gross * 3m - 0.5m, MidpointRounding.AwayFromZero));

        Apply(update, new Price { Gross = 1m }, p => p.Net).Should().Be(-4m, "-3.5 rounds away from zero");
    }

    [Fact]
    public void A_nullable_decimal_property_may_be_the_target()
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.MaybeNet, x => x.Gross * 2m);

        var row = new Price { Gross = 2m };
        update.ApplyTo(row);
        row.MaybeNet.Should().Be(4m);
    }

    [Fact]
    public void A_rounding_without_an_explicit_mode_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2));

        act.Should().Throw<ArgumentException>().WithMessage("*MidpointRounding*");
    }

    [Theory]
    [InlineData(MidpointRounding.ToZero)]
    [InlineData(MidpointRounding.ToNegativeInfinity)]
    [InlineData(MidpointRounding.ToPositiveInfinity)]
    public void Directed_rounding_modes_are_refused(MidpointRounding mode)
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Round(x.Gross, 2, mode));

        act.Should().Throw<ArgumentException>().WithMessage($"*{mode}*");
    }

    [Fact]
    public void Digits_that_depend_on_the_row_are_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Round(x.Gross, x.Count, MidpointRounding.ToEven));

        act.Should().Throw<ArgumentException>().WithMessage("*digits*");
    }

    [Fact]
    public void A_nullable_decimal_operand_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => x.MaybeGross!.Value * 2m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_non_decimal_operand_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross * (decimal)x.Ratio);

        act.Should().Throw<ArgumentException>().WithMessage("*not supported*");
    }

    [Fact]
    public void A_nested_member_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Child.Amount * 2m);

        act.Should().Throw<ArgumentException>().WithMessage("*top-level*");
    }

    [Fact]
    public void Modulo_and_method_calls_other_than_round_are_refused()
    {
        new Action(() => new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross % 2m)).Should().Throw<ArgumentException>();
        new Action(() => new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Abs(x.Gross))).Should().Throw<ArgumentException>();
        new Action(() => new PropertyUpdate<Price>().Compute(x => x.Net, x => decimal.Truncate(x.Gross))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_target_that_is_not_a_top_level_property_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Child.Amount, x => x.Gross);

        act.Should().Throw<ArgumentException>().WithMessage("*top-level*");
    }

    [Fact]
    public void A_formula_reading_a_column_the_update_already_assigns_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Set(x => x.Gross, 5m).Compute(x => x.Net, x => x.Gross / 2m);

        act.Should().Throw<InvalidOperationException>().WithMessage("*reads 'Gross'*");
    }

    [Fact]
    public void Assigning_a_column_an_earlier_formula_reads_is_refused()
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross / 2m);

        new Action(() => update.Set(x => x.Gross, 5m)).Should().Throw<InvalidOperationException>().WithMessage("*reads 'Gross'*");
        new Action(() => update.Increment(x => x.Gross, 1m)).Should().Throw<InvalidOperationException>();
        new Action(() => update.Compute(x => x.Gross, x => x.Net)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_formula_reading_its_own_target_is_refused()
    {
        var act = () => new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Net * 2m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_computed_target_cannot_have_another_assignment()
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross / 2m);

        new Action(() => update.Set(x => x.Net, 1m)).Should().Throw<InvalidOperationException>();
        new Action(() => new PropertyUpdate<Price>().Set(x => x.Net, 1m).Compute(x => x.Net, x => x.Gross)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Independent_assignments_combine()
    {
        // The Symbio shape (TASK-1063): a constant rate beside a price recomputed from the other price.
        var update = new PropertyUpdate<Price>()
            .Set(x => x.Name, "rate changed")
            .Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, MidpointRounding.ToEven));

        var row = new Price { Gross = 120m };
        update.ApplyTo(row);
        row.Name.Should().Be("rate changed");
        row.Net.Should().Be(100m);
    }

    [Fact]
    public void A_division_by_zero_throws_rather_than_storing_anything()
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross / 0m);

        new Action(() => update.ApplyTo(new Price { Gross = 1m })).Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void Clone_keeps_the_computed_assignment()
    {
        var update = new PropertyUpdate<Price>().Compute(x => x.Net, x => x.Gross * 2m);

        var row = new Price { Gross = 3m };
        update.Clone().ApplyTo(row);
        row.Net.Should().Be(6m);
    }
}
