using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Patterns.Paging;
using Birko.Data.Patterns.Specification;
using Birko.Data.Repositories;
using Birko.Data.Stores;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// TASK-171, the medium findings in <c>specifications-and-paging</c>. A rule's two engines must select the same
/// entities: <c>ToExpression()</c> (the store filter) and <c>IsSatisfiedBy</c> (the in-memory evaluator).
/// </summary>
public class SpecificationPagingTriageTests
{
    public enum St { Open, Closed }

    public class W : AbstractModel
    {
        public string? Name { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
        public St State { get; set; }
    }

    private static readonly Guid FirstGuid = Guid.NewGuid();

    private static List<W> Sample() => new()
    {
        new() { Guid = FirstGuid, Name = "Open", Qty = 11, Price = 1.5m, State = St.Open },
        new() { Guid = Guid.NewGuid(), Name = "beta", Qty = 10, Price = 2m, State = St.Closed },
        new() { Guid = Guid.NewGuid(), Name = null, Qty = 3, Price = 3m, State = St.Closed },
    };

    private static (int[] Store, int[] Memory) Both(IRule rule)
    {
        var spec = new RuleSpecification<W>(rule);
        var compiled = spec.ToExpression().Compile();
        int[] Pick(Func<W, bool> f) => Sample().Select((w, i) => (w, i)).Where(t => f(t.w)).Select(t => t.i).ToArray();
        return (Pick(compiled), Pick(spec.IsSatisfiedBy));
    }

    private static void Agree(IRule rule, params int[] expected)
    {
        var (store, memory) = Both(rule);
        store.Should().Equal(expected, "the store filter");
        memory.Should().Equal(expected, "the in-memory evaluator");
    }

    /// <summary>SH-M290: Constant(null, int) threw out of ToExpression. An int is never null.</summary>
    [Fact]
    public void A_null_check_on_a_non_nullable_value_answers_instead_of_throwing()
    {
        Agree(new Rules.Rule(nameof(W.Qty), ComparisonOperator.IsNull, null));
        Agree(new Rules.Rule(nameof(W.Qty), ComparisonOperator.IsNotNull, null), 0, 1, 2);
    }

    /// <summary>SH-M291: an ordering with no operator for the type threw out of ToExpression; it degrades.</summary>
    [Fact]
    public void An_ordering_with_no_operator_for_the_type_degrades_instead_of_throwing()
    {
        Both(new Rules.Rule(nameof(W.Name), ComparisonOperator.GreaterThan, "a")).Store.Should().BeEmpty();
        Both(Rules.Rule.Between(nameof(W.Name), "a", "c")).Store.Should().BeEmpty();
    }

    /// <summary>SH-M292: a Guid or enum given as text could not be converted and matched nothing.</summary>
    [Fact]
    public void A_guid_or_an_enum_given_as_text_is_converted()
    {
        Agree(new Rules.Rule(nameof(W.Guid), ComparisonOperator.Equal, FirstGuid.ToString()), 0);
        Agree(new Rules.Rule(nameof(W.State), ComparisonOperator.Equal, "open"), 0);
    }

    /// <summary>SH-M293: the value was parsed in the host's culture; "1.5" failed on sk-SK.</summary>
    [Fact]
    public void A_number_given_as_text_is_parsed_invariantly_whatever_the_host_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("sk-SK");
        try
        {
            Agree(new Rules.Rule(nameof(W.Price), ComparisonOperator.Equal, "1.5"), 0);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>SH-M294: ChangeType(10.7, int) is 11, so `Qty == 10.7` selected Qty 11. Compared in double now.</summary>
    [Fact]
    public void A_fractional_value_against_an_integral_member_is_not_rounded()
    {
        Agree(new Rules.Rule(nameof(W.Qty), ComparisonOperator.Equal, 10.7));
        Agree(new Rules.Rule(nameof(W.Qty), ComparisonOperator.GreaterThan, 10.5), 0);
    }

    /// <summary>SH-M296: a null needle was "", which every string contains; negated, it inverted again in memory.</summary>
    [Fact]
    public void A_null_needle_matches_nothing_in_either_polarity_on_both_engines()
    {
        Agree(new Rules.Rule(nameof(W.Name), ComparisonOperator.Contains, null));
        Agree(new Rules.Rule(nameof(W.Name), ComparisonOperator.NotContains, null));
    }

    /// <summary>SH-M298: a composite compiled the combined expression, bypassing the operand's evaluator.</summary>
    [Fact]
    public void A_composite_evaluates_through_its_operands()
    {
        // The evaluator compares strings ignoring case and the expression does not (SH-M295, owned by TASK-144):
        // that difference is what makes the dispatch observable.
        var rule = new RuleSpecification<W>(new Rules.Rule(nameof(W.Name), ComparisonOperator.Equal, "open"));
        var always = new RuleSpecification<W>(new Rules.Rule(nameof(W.Qty), ComparisonOperator.GreaterThan, 0));
        var never = new RuleSpecification<W>(new Rules.Rule(nameof(W.Qty), ComparisonOperator.LessThan, 0));

        var alone = Sample().Count(rule.IsSatisfiedBy);
        alone.Should().Be(1);
        Sample().Count(rule.And(always).IsSatisfiedBy).Should().Be(alone);
        Sample().Count(rule.Or(never).IsSatisfiedBy).Should().Be(alone);
        Sample().Count(rule.Not().IsSatisfiedBy).Should().Be(Sample().Count - alone);
    }

    // ---- paging ----

    private sealed class Recording : AbstractBulkRepository<W>
    {
        public OrderBy<W>? LastOrder;
        public int? LastOffset;

        public Recording(InMemoryStore<W> store) : base(store) { }

        public override IEnumerable<W> Read(Expression<Func<W, bool>>? filter = null, OrderBy<W>? orderBy = null, int? limit = null, int? offset = null)
        {
            LastOrder = orderBy;
            LastOffset = offset;
            return base.Read(filter, orderBy, limit, offset);
        }
    }

    private static Recording Repository()
    {
        var store = new InMemoryStore<W>();
        foreach (var w in Sample()) store.Create(w);
        return new Recording(store);
    }

    /// <summary>SH-M300: (page - 1) * pageSize wrapped to a negative offset. A page past every row is empty.</summary>
    [Fact]
    public void A_page_beyond_any_possible_offset_is_empty_and_never_negative()
    {
        var repository = Repository();
        var page = new PagedRepositoryWrapper<W>(repository).ReadPaged(page: int.MaxValue, pageSize: 20);

        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(3);
        (repository.LastOffset is null or >= 0).Should().BeTrue();
    }

    /// <summary>SH-M301: paging with no order sent LIMIT/OFFSET with no ORDER BY. The Guid makes the order total.</summary>
    [Fact]
    public void Paging_always_orders_totally_and_keeps_the_callers_order_first()
    {
        var repository = Repository();
        var wrapper = new PagedRepositoryWrapper<W>(repository);

        wrapper.ReadPaged(page: 1, pageSize: 2);
        repository.LastOrder!.Fields.Select(f => f.PropertyName).Should().Equal(nameof(W.Guid));

        var callers = OrderBy<W>.ByDescending(x => x.Qty);
        wrapper.ReadPaged(orderBy: callers, page: 1, pageSize: 2);
        repository.LastOrder!.Fields.Select(f => (f.PropertyName, f.Descending)).Should().Equal((nameof(W.Qty), true), (nameof(W.Guid), false));
        callers.Fields.Should().HaveCount(1, "the order the caller passed is copied, not modified");
    }
}
