using System;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SchemaDrift;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-510 — <c>EnsureColumns</c> against a real on-disk SQLite database: a table created before its model
/// grew columns gains them, and the rows that were already there read back as <c>default(T)</c>.
/// <para>
/// SQLite is the provider that made this a task: it refuses <c>ADD COLUMN … NOT NULL</c> with no
/// <c>DEFAULT</c> on a table with rows ("Cannot add a NOT NULL column with default value NULL"), so before the
/// fix even a consumer that detected the gap could not close it through the framework. Every value type the
/// field factory maps is in <see cref="After"/>, because the default's stored <i>shape</i> differs per type
/// (<c>TimeOnly</c> is text, a <c>[UtcField]</c> is an offset timestamp) and a wrong shape reads back wrong.
/// </para>
/// </summary>
public class EnsureColumnsEndToEndTests : IDisposable
{
    private const string TableName = "EnsureColumnsUpgrade";
    private readonly string _root;

    public EnsureColumnsEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-ensure-columns-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public enum Stage { Draft = 0, Live = 1 }

    /// <summary>The table as an old release created it.</summary>
    [Table(TableName)]
    public class Before : AbstractModel
    {
        public string? Name { get; set; }
    }

    /// <summary>The same table as the current model declares it.</summary>
    [Table(TableName)]
    public class After : AbstractModel
    {
        public string? Name { get; set; }
        public bool Flag { get; set; }
        public int Count { get; set; }
        public long Total { get; set; }
        public short Small { get; set; }
        public double Cost { get; set; }
        public float Weight { get; set; }
        public decimal Amount { get; set; }
        [PrecisionField(18)]
        [ScaleField(2)]
        public decimal Price { get; set; }
        public Guid Reference { get; set; }
        public DateTime Seen { get; set; }
        [UtcField]
        public DateTime SeenUtc { get; set; }
        public TimeOnly Opens { get; set; }
        public Stage State { get; set; }
        public int? Optional { get; set; }
        [RequiredField]
        public int? RequiredNullable { get; set; }
        public string? Note { get; set; }
    }

    [Table(TableName)]
    public class WithUnique : AbstractModel
    {
        public string? Name { get; set; }
        public int Count { get; set; }
        [UniqueField]
        public int Code { get; set; }
    }

    [Table(TableName)]
    public class WithRequiredString : AbstractModel
    {
        public string? Name { get; set; }
        public int Count { get; set; }
        [RequiredField]
        public string Title { get; set; } = string.Empty;
    }

    public class NotMapped
    {
        public int Value { get; set; }
    }

    private SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "ensure.db" });
        return (SqLiteConnector)factory.GetConnector();
    }

    private static System.Collections.Generic.List<T> Rows<T>(SqLiteConnector connector)
        => connector.Select(typeof(T), (System.Linq.Expressions.LambdaExpression?)null).Cast<T>().ToList();

    private (SqLiteConnector Connector, Guid Existing) OldTableWithARow()
    {
        var connector = Connector();
        connector.CreateTable(new[] { typeof(Before) });
        var existing = Guid.NewGuid();
        connector.Insert(new Before { Guid = existing, Name = "old" });
        return (connector, existing);
    }

    [Fact]
    public void An_old_table_with_rows_gains_every_missing_column_and_old_rows_read_back_as_defaults()
    {
        var (connector, existing) = OldTableWithARow();

        var added = connector.EnsureColumns(typeof(After));

        added.Select(d => d.Column).Should().BeEquivalentTo(
            "Flag", "Count", "Total", "Small", "Cost", "Weight", "Amount", "Price", "Reference", "Seen",
            "SeenUtc", "Opens", "State", "Optional", "RequiredNullable", "Note");
        added.Should().OnlyContain(d => d.Kind == ColumnDriftKind.Missing);

        var row = Rows<After>(connector).Single(x => x.Guid == existing);
        row.Name.Should().Be("old", "an existing value must survive the ALTER");
        // RequiredNullable is the one exception: [RequiredField] makes the column NOT NULL, so null cannot be
        // stored and the back-fill is the underlying type's default.
        row.Should().BeEquivalentTo(new After { Guid = existing, Name = "old", RequiredNullable = 0 },
            "an old row must read back exactly as an entity that never assigned the new properties");
        row.SeenUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void After_ensuring_the_table_reports_clean_and_takes_new_rows()
    {
        var (connector, _) = OldTableWithARow();

        connector.EnsureColumns(typeof(After));

        connector.DetectDrift(typeof(After)).IsClean.Should().BeTrue(
            "every added column comes from the same ConvertType CREATE TABLE uses");

        var fresh = new After
        {
            Guid = Guid.NewGuid(), Name = "new", Flag = true, Count = 3, Total = 4, Small = 5, Cost = 1.5,
            Weight = 2.5f, Amount = 7m, Price = 9.25m, Reference = Guid.NewGuid(),
            Seen = new DateTime(2026, 10, 3, 12, 30, 0), SeenUtc = new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc),
            Opens = new TimeOnly(8, 0), State = Stage.Live, Optional = 11, RequiredNullable = 12, Note = "n",
        };
        connector.Insert(fresh);

        Rows<After>(connector).Single(x => x.Guid == fresh.Guid)
            .Should().BeEquivalentTo(fresh);
    }

    [Fact]
    public void A_second_run_adds_nothing()
    {
        var (connector, _) = OldTableWithARow();
        connector.EnsureColumns(typeof(After));

        connector.EnsureColumns(typeof(After)).Should().BeEmpty();
    }

    [Fact]
    public void A_table_that_does_not_exist_is_left_for_CREATE_TABLE()
    {
        var connector = Connector();

        connector.EnsureColumns(typeof(After)).Should().BeEmpty();

        connector.DetectDrift(typeof(After)).TableExists.Should().BeFalse();
    }

    [Fact]
    public void A_unique_column_is_refused_and_nothing_is_altered()
    {
        var (connector, _) = OldTableWithARow();

        var act = () => connector.EnsureColumns(typeof(WithUnique));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Code: it is unique*");
        connector.DetectDrift(typeof(WithUnique)).Drifts.Select(d => d.Column)
            .Should().BeEquivalentTo(new[] { "Count", "Code" },
                "the refusal is decided before any DDL, so the addable Count column was not added either");
    }

    [Fact]
    public void A_required_string_is_refused_rather_than_back_filled_with_an_invented_value()
    {
        var (connector, _) = OldTableWithARow();

        var act = () => connector.EnsureColumns(typeof(WithRequiredString));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Title: it is NOT NULL and has no value-type default*");
        connector.DetectDrift(typeof(WithRequiredString)).Drifts.Should().HaveCount(2);
    }

    [Fact]
    public void An_unmapped_type_is_refused_rather_than_reported_as_nothing_missing()
    {
        var connector = Connector();

        var act = () => connector.EnsureColumns(typeof(NotMapped));

        act.Should().Throw<NotSupportedException>().WithMessage("*not a mapped entity*");
    }

    [Fact]
    public void Columns_are_only_added_never_dropped_or_retyped()
    {
        var connector = Connector();
        connector.CreateTable(new[] { typeof(After) });
        var existing = Guid.NewGuid();
        connector.Insert(new After { Guid = existing, Name = "kept", Count = 42, RequiredNullable = 1 });

        // The model shrinks back to Before: Count and the rest become Unexpected, which is not this call's business.
        connector.EnsureColumns(typeof(Before)).Should().BeEmpty();

        Rows<After>(connector).Single(x => x.Guid == existing).Count.Should().Be(42);
    }
}
