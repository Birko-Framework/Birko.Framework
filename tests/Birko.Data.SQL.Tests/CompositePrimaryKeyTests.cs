using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Fields;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-303 — a composite <c>PRIMARY KEY (a, b)</c> could not be declared at all.
///
/// <para>
/// <c>FieldDefinition</c> rendered <c>PRIMARY KEY</c> inline from each field's flag, so two primary
/// fields emitted <b>two clauses</b>, which every provider rejects. Measured before the fix: PostgreSQL 16
/// answers <c>42P16 multiple primary keys for table "T" are not allowed</c>, and SQLite
/// <c>Error 1: table has more than one primary key</c> — the task had only recorded the PostgreSQL half.
/// </para>
///
/// <para>
/// <b>Why it matters rather than being a missing nicety.</b> TimescaleDB <i>requires</i> a composite key
/// for the natural telemetry shape. Measured on 2.29.2: <c>create_hypertable</c> over a Guid-keyed table
/// answers <c>cannot create a unique index without the column "ts" (used in partitioning)</c> and hints
/// "ensure the partitioning column is part of the primary or composite key", while the same table with
/// <c>PRIMARY KEY (Guid, Ts)</c> converts cleanly. So the framework could not express a shape one of its
/// own providers demands.
/// </para>
///
/// <para>
/// ⚠ <b>Blast radius, measured before implementing:</b> <b>0</b> classes across the framework, its tests
/// and all 16 consumer repos declare more than one primary. So this is purely additive — the opposite of
/// TASK-248, where the same instinct was vetoed because seven live entities would have broken.
/// </para>
/// </summary>
public class CompositePrimaryKeyTests
{
    [Table("SinglePk")]
    public class SingleKeyRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
    }

    [Table("CompositePk")]
    public class CompositeKeyRow
    {
        [PrimaryField]
        public Guid? Guid { get; set; }

        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }

        public double Value { get; set; }
    }

    /// <summary>
    /// The declaration-order property, asserted because a dictionary could reorder it and a composite
    /// key's column order decides which range scans its index can serve.
    /// </summary>
    [Table("CompositeOrder")]
    public class OrderedKeyRow
    {
        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }

        [PrimaryField]
        public Guid? Guid { get; set; }
    }

    private static Birko.Data.SQL.Tables.Table Load<T>() => Birko.Data.SQL.DataBase.LoadTable(typeof(T));

    [Fact]
    public void A_single_primary_field_still_renders_inline()
    {
        var field = Load<SingleKeyRow>().Fields.Values.Single(x => x.IsPrimary);

        field.UsesInlinePrimaryConstraint.Should().BeTrue(
            "one primary field is the overwhelmingly common case and its DDL must not change");
    }

    /// <summary>
    /// The suppression that makes the table-level clause possible. Both fields must stop rendering their
    /// own <c>PRIMARY KEY</c>, or the statement carries three clauses instead of one.
    /// </summary>
    [Fact]
    public void Every_field_of_a_composite_key_suppresses_its_inline_clause()
    {
        var primary = Load<CompositeKeyRow>().Fields.Values.Where(x => x.IsPrimary).ToList();

        primary.Should().HaveCount(2);
        primary.Should().OnlyContain(x => !x.UsesInlinePrimaryConstraint);
    }

    /// <summary>
    /// A non-primary field is unaffected — the flag is about which shape carries the key, not about
    /// whether a column has one.
    /// </summary>
    [Fact]
    public void A_non_primary_field_never_uses_the_inline_primary_form()
    {
        var value = Load<CompositeKeyRow>().Fields.Values.Single(x => x.Name == nameof(CompositeKeyRow.Value));

        value.UsesInlinePrimaryConstraint.Should().BeFalse();
    }

    /// <summary>
    /// ⚠ The migrations path constructs a <c>SchemaField</c> with <b>no table</b>, and each field is
    /// rendered on its own there — so a null table must keep the inline form rather than silently
    /// suppressing a key nothing else will emit.
    /// </summary>
    [Fact]
    public void A_field_with_no_table_keeps_the_inline_form()
    {
        var orphan = new StringField(null!, "Solo", primary: true);

        orphan.Table.Should().BeNull("this is the shape the migrations schema builder constructs");
        orphan.UsesInlinePrimaryConstraint.Should().BeTrue();
    }

    /// <summary>
    /// Declaration order, not dictionary order: <c>(Ts, Guid)</c> here, the reverse of the other model.
    /// </summary>
    [Fact]
    public void The_key_columns_keep_their_declaration_order()
    {
        var primary = Load<OrderedKeyRow>().Fields.Values.Where(x => x.IsPrimary).Select(x => x.Name).ToList();

        primary.Should().Equal(new[] { nameof(OrderedKeyRow.Ts), nameof(OrderedKeyRow.Guid) });
    }

    // ── the EMITTED DDL, not just the flag ───────────────────────────────────────────────────

    /// <summary>
    /// A connector that records the column definitions it is handed, so the rendered statement can be
    /// asserted without a database.
    /// </summary>
    private sealed class RecordingConnector : Birko.Data.SQL.Connectors.AbstractConnector
    {
        public RecordingConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public readonly List<string> Fields = new();

        protected override void CreateTableCore(string name, IEnumerable<string> fields)
            => Fields.AddRange(fields);

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, AbstractField field) => "TEXT";
        public override string FieldDefinition(AbstractField field)
            => field.Name + " TEXT" + (field.UsesInlinePrimaryConstraint ? " PRIMARY KEY" : string.Empty);
    }

    private static List<string> Emit<T>()
    {
        var c = new RecordingConnector();
        c.CreateTable(new[] { typeof(T) });
        return c.Fields;
    }

    /// <summary>
    /// ⚠ <b>Added because a mutation exposed the gap.</b> Deleting the table-level clause left this whole
    /// project green — every other test here asserts the suppression flag, and none asserted that anything
    /// replaces the inline clauses it removes. Suppression without emission is strictly worse than the
    /// original defect: the table would be created with <b>no key at all</b>, silently, and bulk update and
    /// delete key on <c>GetPrimaryFields()</c> and would quietly do nothing.
    /// </summary>
    [Fact]
    public void A_composite_key_is_emitted_as_one_table_level_clause()
    {
        var emitted = Emit<CompositeKeyRow>();

        emitted.Should().ContainSingle(x => x.StartsWith("PRIMARY KEY", StringComparison.Ordinal))
               .Which.Should().Be("PRIMARY KEY (Guid, Ts)");

        emitted.Where(x => !x.StartsWith("PRIMARY KEY", StringComparison.Ordinal))
               .Should().OnlyContain(x => !x.Contains("PRIMARY KEY"),
                   "no column may also carry an inline clause, or the statement declares two keys");
    }

    /// <summary>
    /// The single-key case must emit no table-level clause at all — otherwise every existing entity's DDL
    /// changes, which is the behaviour-preservation half.
    /// </summary>
    [Fact]
    public void A_single_key_is_still_emitted_inline_with_no_table_level_clause()
    {
        var emitted = Emit<SingleKeyRow>();

        emitted.Should().NotContain(x => x.StartsWith("PRIMARY KEY", StringComparison.Ordinal));
        emitted.Should().ContainSingle(x => x.Contains("PRIMARY KEY"))
               .Which.Should().StartWith("Guid ");
    }

    /// <summary>
    /// SQLite's <c>INTEGER PRIMARY KEY AUTOINCREMENT</c> is a single-column form by construction: measured,
    /// it is rejected alongside a table-level clause with <c>"table has more than one primary key"</c>.
    /// Refused up front, because the server's own message says nothing about autoincrement.
    /// </summary>
    [Fact]
    public void A_composite_key_including_an_auto_increment_column_is_refused()
    {
        Action act = () => Emit<AutoIncrementCompositeRow>();

        act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
           .WithMessage("*auto-increment*");
    }

    [Table("CompositeAuto")]
    public class AutoIncrementCompositeRow
    {
        [PrimaryField]
        [IncrementField]
        public int Id { get; set; }

        [PrimaryField]
        [RequiredField]
        public DateTime Ts { get; set; }
    }
}
