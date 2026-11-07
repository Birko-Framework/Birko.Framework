using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-865 — a lifted ordering (<c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>) with a null operand is false in C#, so
/// its negation is true of every entity. Before, <see cref="PredicateScope"/> knew only the empty-<c>Contains</c> group,
/// and <c>DeleteAsync(x =&gt; !(x.Score &gt; nul))</c> deleted 6 of 6 rows on InMemory with no exception (and the same on
/// JSON, XML, MongoDB, RavenDB, Cosmos DB and Elasticsearch: <c>NullOrderingScopeContract</c>).
/// <para><c>==</c> / <c>!=</c> null are ordinary filters and are never claimed.</para>
/// </summary>
public class PredicateScopeNullOrderingTests
{
    private class Row
    {
        public int A { get; set; }
        public int? Score { get; set; }
        public int? Other { get; set; }
        public decimal? Price { get; set; }
        public DateTime? At { get; set; }
        public Money? Cost { get; set; }
    }

    /// <summary>A user-defined ordering declared on NULLABLE operands: not lifted, so it decides what null means.</summary>
    private readonly struct Money
    {
        public Money(int value) => Value = value;
        public int Value { get; }
        public static bool operator >(Money? left, Money? right) => (left?.Value ?? 0) > (right?.Value ?? 0);
        public static bool operator <(Money? left, Money? right) => (left?.Value ?? 0) < (right?.Value ?? 0);
    }

    private static readonly int? Nul = null;
    private static readonly int? Five = 5;
    private static readonly decimal? NulDecimal = null;
    private static readonly DateTime? NulDate = null;
    private static readonly Money? NulMoney = null;

    private static bool Reduces(Expression<Func<Row, bool>> e) => PredicateScope.ReducesToAllRows(e);

    [Fact]
    public void A_negated_ordering_against_a_captured_null_covers_everything()
    {
        Reduces(x => !(x.Score > Nul)).Should().BeTrue();
        Reduces(x => !(x.Score >= Nul)).Should().BeTrue();
        Reduces(x => !(x.Score < Nul)).Should().BeTrue();
        Reduces(x => !(x.Score <= Nul)).Should().BeTrue();
    }

    [Fact]
    public void The_null_may_stand_on_either_side()
    {
        Reduces(x => !(Nul < x.Score)).Should().BeTrue();
        Reduces(x => !(Nul >= x.Score)).Should().BeTrue();
    }

    [Fact]
    public void A_literal_null_counts_as_a_captured_one()
        => Reduces(x => !(x.Score <= (int?)null)).Should().BeTrue();

    [Fact]
    public void A_non_nullable_column_lifted_against_null_counts()
        => Reduces(x => !(x.A > Nul)).Should().BeTrue();

    [Fact]
    public void A_lifted_operator_method_counts_decimal_and_DateTime()
    {
        Reduces(x => !(x.Price > NulDecimal)).Should().BeTrue();
        Reduces(x => !(x.At < NulDate)).Should().BeTrue();
    }

    [Fact]
    public void The_null_ordering_is_found_inside_a_group_and_under_nesting()
    {
        Reduces(x => !(x.Score > Nul && x.A > 3)).Should().BeTrue();
        Reduces(x => !(x.A > 0 && (x.A > 1 && x.Score >= Nul))).Should().BeTrue();
        Reduces(x => x.A > 3 || !(x.Score < Nul && x.A > 1)).Should().BeTrue();
        Reduces(x => !(x.Score > Nul || x.Score < Nul)).Should().BeTrue();
        Reduces(x => !(!(!(x.Score > Nul && x.A > 0)) && x.A > 1)).Should().BeTrue();
    }

    [Fact]
    public void Equality_with_null_is_an_ordinary_filter()
    {
        Reduces(x => x.Score != Nul).Should().BeFalse();
        Reduces(x => x.Score == Nul).Should().BeFalse();
        Reduces(x => !(x.Score == Nul)).Should().BeFalse();
        Reduces(x => !(x.Score != Nul)).Should().BeFalse();
    }

    [Fact]
    public void A_null_ordering_that_does_not_decide_the_group_still_constrains()
    {
        Reduces(x => !(x.Score > Nul || x.A > 3)).Should().BeFalse();
        Reduces(x => x.Score > Nul || x.A > 4).Should().BeFalse();
        Reduces(x => !(x.Score > Nul) && x.A > 4).Should().BeFalse();
    }

    [Fact]
    public void An_ordering_against_a_value_or_a_column_is_not_claimed()
    {
        Reduces(x => !(x.Score > Five)).Should().BeFalse();
        Reduces(x => !(x.Score > x.Other)).Should().BeFalse();
    }

    [Fact]
    public void A_bare_null_ordering_matches_nothing_rather_than_everything()
        => Reduces(x => x.Score > Nul).Should().BeFalse();

    [Fact]
    public void A_user_defined_ordering_on_nullable_operands_is_not_claimed()
    {
        Reduces(x => !(x.Cost > NulMoney)).Should().BeFalse("the operator is not lifted; it defines null itself");
        Reduces(x => !(x.Cost < NulMoney)).Should().BeFalse();
    }

    [Fact]
    public void A_negated_null_ordering_is_not_the_explicit_all_rows_door()
        => PredicateScope.IsExplicitAllRows((Expression<Func<Row, bool>>)(x => !(x.Score > Nul))).Should().BeFalse();
}
