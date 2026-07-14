using System;
using Birko.Data.RavenDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.RavenDB.Views.Tests;

/// <summary>
/// CR-H079: the RavenDB.Views backend shipped with no tests; CR-H078: the Avg reduce was not
/// re-reduce-safe. These offline tests pin RavenViewTranslator.TranslateToMapReduce, especially the
/// running-sum Avg pattern that survives RavenDB's incremental re-reduce.
/// </summary>
public class RavenViewTranslatorTests
{
    private class Sale
    {
        public Guid? Guid { get; set; }
        public string Region { get; set; } = string.Empty;
        public decimal Amount { get; set; }
    }

    private class RegionSalesView
    {
        public string Region { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Average { get; set; }
        public long Count { get; set; }
    }

    [Fact]
    public void Null_definition_throws()
    {
        Action act = () => RavenViewTranslator.TranslateToMapReduce(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NonAggregate_view_has_null_reduce()
    {
        var def = new ViewDefinitionBuilder<RegionSalesView>()
            .From<Sale>()
            .Select<Sale, string>(s => s.Region, v => v.Region)
            .Build();

        var (map, reduce) = RavenViewTranslator.TranslateToMapReduce(def);

        map.Should().Contain("from entity in docs.Sale");
        reduce.Should().BeNull();
    }

    [Fact]
    public void Avg_reduce_is_reReduce_safe()
    {
        var def = new ViewDefinitionBuilder<RegionSalesView>()
            .From<Sale>()
            .GroupBy<Sale, string>(s => s.Region)
            .Select<Sale, string>(s => s.Region, v => v.Region)
            .Avg<Sale, decimal>(s => s.Amount, v => v.Average)
            .Build();

        var (map, reduce) = RavenViewTranslator.TranslateToMapReduce(def);

        // Map must carry a running sum + count, not just the raw averaged field.
        map.Should().Contain("Average_Sum = entity.Amount");
        map.Should().Contain("Average_Count = 1");

        // Reduce must sum the running Sum/Count (stable) and recompute the ratio — never re-sum the
        // already-averaged Average field.
        reduce.Should().NotBeNull();
        reduce!.Should().Contain("Average_Sum = g.Sum(x => x.Average_Sum)");
        reduce.Should().Contain("Average_Count = g.Sum(x => x.Average_Count)");
        reduce.Should().Contain("Average = g.Sum(x => x.Average_Sum) / g.Sum(x => x.Average_Count)");
        reduce.Should().NotContain("g.Sum(x => x.Average)");   // the double-average bug
    }

    [Fact]
    public void Sum_and_count_reduce_use_sum_of_sums()
    {
        var def = new ViewDefinitionBuilder<RegionSalesView>()
            .From<Sale>()
            .GroupBy<Sale, string>(s => s.Region)
            .Select<Sale, string>(s => s.Region, v => v.Region)
            .Sum<Sale, decimal>(s => s.Amount, v => v.Total)
            .Count<Sale>(v => v.Count)
            .Build();

        var (_, reduce) = RavenViewTranslator.TranslateToMapReduce(def);

        reduce!.Should().Contain("Total = g.Sum(x => x.Total)");
        reduce.Should().Contain("Count = g.Sum(x => x.Count)");
    }

    private class GlobalCountView
    {
        public long Count { get; set; }
    }

    // CR-L168: an aggregate-only view with neither GroupBy clauses nor regular Fields must emit a valid
    // global aggregate (`group result by 1 into g`), not the malformed `group result by new {  } into g`
    // that an empty composite key would produce.
    [Fact]
    public void Aggregate_only_view_with_no_groupby_or_fields_groups_by_constant()
    {
        var def = new ViewDefinitionBuilder<GlobalCountView>()
            .From<Sale>()
            .Count<Sale>(v => v.Count)
            .Build();

        var (_, reduce) = RavenViewTranslator.TranslateToMapReduce(def);

        reduce.Should().NotBeNull();
        reduce!.Should().Contain("group result by 1 into g");
        reduce.Should().NotContain("group result by new {  }");
        reduce.Should().Contain("Count = g.Sum(x => x.Count)");
    }
}
