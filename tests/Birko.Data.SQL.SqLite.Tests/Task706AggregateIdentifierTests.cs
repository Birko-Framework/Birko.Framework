using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-706 (SH-T696-01) — an aggregate query's caller-supplied names, against a real SQLite file.
///
/// <para><c>BuildAggregateQueryParts</c> resolved a group-by field, an aggregate's source and the time column through
/// <c>ResolveSqlName</c>, which fell back to the raw string when no property matched, so the string reached the SQL
/// text as written. <c>SelectAggregate</c> passed <c>query.OrderBy</c> to <c>CreateSelectCommand</c> unresolved, the
/// sink TASK-110 closed for ordinary reads. A time bucket rendered TimescaleDB's <c>time_bucket</c> on every provider,
/// with an unparseable interval inside its quotes as written. Rules 15, 33 and 45: a name is resolved against metadata
/// or refused; a provider that cannot express something refuses it (rule 41).</para>
/// </summary>
public class Task706AggregateIdentifierTests : IDisposable
{
    private readonly string _root;

    public Task706AggregateIdentifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-agg706-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        SqlitePool.ClearForDirectory(_root);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("AggRows")]
    public class AggRow : AbstractModel
    {
        public string? Name { get; set; }

        [NamedField("amount_col")]
        public int Amount { get; set; }

        public DateTime At { get; set; }
    }

    private SQLiteStore<AggRow> NewStore()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "agg.db" });
        ((Birko.Data.SQL.Connectors.SqLiteConnector)factory.GetConnector()).CreateTable(new[] { typeof(AggRow) });
        var store = new SQLiteStore<AggRow>();
        store.SetSettings(new SqLiteSettings(_root, "agg.db"));
        store.Create(new List<AggRow>
        {
            new() { Name = "a", Amount = 1, At = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc) },
            new() { Name = "a", Amount = 2, At = new DateTime(2026, 1, 1, 11, 0, 0, DateTimeKind.Utc) },
            new() { Name = "bb", Amount = 4, At = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc) },
        });
        return store;
    }

    private AsyncSQLiteStore<AggRow> NewAsyncStore()
    {
        NewStore();
        var store = new AsyncSQLiteStore<AggRow>();
        store.SetSettings(new SqLiteSettings(_root, "agg.db"));
        return store;
    }

    // ── What still works ──

    [Fact]
    public void Group_by_and_sum_over_mapped_properties_works_and_orders_by_alias_and_group()
    {
        var rows = NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            GroupByFields = new[] { "Name" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount", "total") },
            OrderBy = new Dictionary<string, bool> { ["total"] = true },
        });

        rows.Select(r => (r.Values["Name"], Convert.ToInt64(r.Values["total"]))).Should().Equal(("bb", 4L), ("a", 3L));

        var byGroup = NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            GroupByFields = new[] { "Name" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount") },
            OrderBy = new Dictionary<string, bool> { ["Name"] = false },
        });
        byGroup.Select(r => r.Values["Name"]).Should().Equal("a", "bb");
    }

    // ── Names that are no property are refused, not interpolated ──

    [Fact]
    public void An_aggregate_source_that_is_no_property_is_refused()
    {
        // Measured before the fix: SUM(length(Name)) ran, no error — the string was SQL.
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "length(Name)", "x") },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*length(Name)*");
    }

    [Fact]
    public void A_group_by_field_that_is_no_property_is_refused()
    {
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            GroupByFields = new[] { "Name || 'x'" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount") },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*Name || 'x'*");
    }

    [Fact]
    public void An_order_by_key_that_is_neither_a_group_field_nor_an_alias_is_refused()
    {
        // Measured before the fix: ORDER BY random() ran.
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            GroupByFields = new[] { "Name" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount") },
            OrderBy = new Dictionary<string, bool> { ["random()"] = false },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*random()*");
    }

    [Fact]
    public async Task The_async_path_refuses_the_same_names()
    {
        var store = NewAsyncStore();
        var source = async () => await store.AggregateAsync(new AggregateQuery<AggRow>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "length(Name)", "x") },
        });
        await source.Should().ThrowAsync<ArgumentException>();

        var order = async () => await store.AggregateAsync(new AggregateQuery<AggRow>
        {
            GroupByFields = new[] { "Name" },
            Aggregates = new[] { new AggregateField(AggregateFunction.Count, "Amount") },
            OrderBy = new Dictionary<string, bool> { ["random()"] = false },
        });
        await order.Should().ThrowAsync<ArgumentException>();
    }

    // ── Time buckets ──

    [Fact]
    public void A_time_bucket_on_a_provider_without_one_is_refused_by_name()
    {
        // Measured before the fix: SqliteException "no such function: time_bucket".
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "At",
            TimeBucketInterval = "1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount") },
        });

        act.Should().Throw<NotSupportedException>().WithMessage("*time bucket*");
    }

    [Theory]
    [InlineData("1 hour', At) AS b, (SELECT 1) AS z --")]
    [InlineData("soon")]
    public void An_unparseable_interval_is_refused(string interval)
    {
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "At",
            TimeBucketInterval = interval,
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount") },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*interval*");
    }

    [Fact]
    public void A_time_column_that_is_no_property_is_refused()
    {
        var act = () => NewStore().Aggregate(new AggregateQuery<AggRow>
        {
            TimeColumn = "datetime('now')",
            TimeBucketInterval = "1 hour",
            Aggregates = new[] { new AggregateField(AggregateFunction.Sum, "Amount") },
        });

        act.Should().Throw<ArgumentException>().WithMessage("*datetime('now')*");
    }
}
