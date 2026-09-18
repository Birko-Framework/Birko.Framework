using System;
using System.Data;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-257 — what an <b>unlengthed</b> <c>string</c> declares on SQL Server, and what changes when an index
/// key names it. This file previously asserted the opposite (<c>TEXT</c>, indexed or not) and called the
/// consequence a known out-of-scope divergence; that premise is gone, so the file is rewritten rather than
/// extended.
///
/// <para>
/// <b>What was wrong.</b> <c>ConvertType</c> mapped every non-<c>CharField</c> string to the deprecated
/// <c>TEXT</c>, and a <c>TEXT</c> column cannot take a parameter comparison — measured against live SQL Server
/// 2022 (16.0.4265.3): <c>= @p</c> / <c>&lt;&gt; @p</c> / <c>IN (@p)</c> raise <b>Msg 402</b>
/// ("the data types text and nvarchar are incompatible"), <c>LOWER(col)</c> raises <b>8116</b>,
/// <c>ORDER BY</c> and <c>GROUP BY</c> raise <b>306</b>, <c>SELECT DISTINCT</c> raises <b>421</b>. Only
/// <c>LIKE</c> and <c>IS NULL</c> were legal. So a plain <c>public string Name { get; set; }</c> — the common
/// consumer shape, present on essentially every consumer entity — broke every predicate and every sort over
/// that column.
/// </para>
/// <para>
/// <b>Why there are two answers and not one.</b> <c>NVARCHAR(MAX)</c> fixes the whole predicate class, but an
/// index key may not be a MAX type at all: measured, an index over <c>NVARCHAR(MAX)</c> raises the <i>same</i>
/// Msg 1919 as one over <c>TEXT</c>. So the MAX change alone would have left every declared index over an
/// unlengthed string exactly as broken, and for <c>UNIQUE</c>/<c>PRIMARY KEY</c> — which
/// <c>FieldDefinition</c> emits as inline column constraints — it took down the whole <c>CREATE TABLE</c>.
/// Hence the bounded branch, gated on <c>AbstractField.IsInIndexKey</c>.
/// </para>
/// <para>
/// Every case goes through <c>DataBase.LoadTable</c> rather than constructing the field by hand: a hand-built
/// field survives a dispatch-only revert, so such a test cannot witness this fix
/// (<c>MSSqlPrimitiveColumnTypeTests</c> states the same reason). The two <c>IsIndexed</c> marking sites in
/// <c>DataBase.LoadIndexes</c> — per-property <c>[IndexedField]</c> and class-level <c>[CompositeIndex]</c> —
/// get <b>one entity each</b>, deliberately: TASK-248's MySQL suite used only the class-level form, so
/// reverting the per-property marking failed 0 tests there.
/// </para>
/// <para>No live server required; <c>ConvertType</c> and <c>FieldDefinition</c> are pure.</para>
/// </summary>
public class IndexedStringColumnTypeTests
{
    // ---- probe entities. Distinct CLR types and table names: DataBase's table cache is static per process.

    /// <summary>Nothing declared — the shape a consumer writes by default.</summary>
    [Table("MsStrPlain")]
    public class PlainEntity : AbstractLogModel
    {
        public string Label { get; set; } = null!;

        [MaxLengthField(64)]
        public string Bounded { get; set; } = null!;

        /// <summary>Second sink into the same mapping arm: TimeOnlyField is not a CharField (TASK-257).</summary>
        public TimeOnly OpensAt { get; set; }
    }

    /// <summary>Per-property index — <c>DataBase_Table.cs</c>'s first <c>IsIndexed</c> marking site.</summary>
    [Table("MsStrPerProp")]
    public class PerPropertyIndexEntity : AbstractLogModel
    {
        [IndexedField("ix_msstrperprop_code")]
        public string Code { get; set; } = null!;

        public string Untouched { get; set; } = null!;
    }

    /// <summary>Class-level composite — the second marking site, and Symbio's real shape.</summary>
    [Table("MsStrComposite")]
    [CompositeIndex("ux_msstrcomposite_number", nameof(Number), IsUnique = true)]
    public class CompositeIndexEntity : AbstractLogModel
    {
        public string Number { get; set; } = null!;

        public string Untouched { get; set; } = null!;
    }

    /// <summary>UNIQUE and PRIMARY KEY are index keys that LoadIndexes never marks.</summary>
    /// <remarks>
    /// TASK-275 — <c>Sku</c> is <c>[RequiredField]</c> deliberately. A <b>nullable</b> unique column no
    /// longer emits an inline <c>UNIQUE</c> at all: it carries a synthesised partial unique index instead,
    /// because the inline form admits only one NULL row on SQL Server. This suite is about the inline path
    /// being bounded, so it keeps a column that still takes it. The nullable shape's bounding is asserted in
    /// <c>NullableUniqueColumnLiveTests</c>, where it matters more — an unbounded column there means the
    /// synthesised index cannot be built at all.
    /// </remarks>
    [Table("MsStrConstraints")]
    public class ConstraintEntity : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public string Sku { get; set; } = null!;

        [PrimaryField]
        public string NaturalKey { get; set; } = null!;
    }

    private static MSSqlConnector Connector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    private static AbstractField Field(Type entity, string property)
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(entity);
        var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
        field.Should().NotBeNull($"'{property}' must map to a column at all");
        return field!;
    }

    private static string TypeOf(Type entity, string property)
        => Connector().ConvertType(Field(entity, property).Type, Field(entity, property));

    // ---- the unindexed default

    [Fact]
    public void An_unlengthed_string_declares_nvarchar_max()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Label))
            .Should().Be("NVARCHAR(MAX)",
                "TEXT cannot take a parameter comparison on SQL Server (Msg 402), and a bounded default "
                + "would start refusing values that a TEXT column accepts today");

    [Fact]
    public void An_explicit_length_still_declares_that_length()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Bounded))
            .Should().Be("NVARCHAR(64)", "[MaxLengthField] is unaffected by this change");

    [Fact]
    public void A_TimeOnly_column_declares_nvarchar_max_not_text()
        // TimeOnlyField is an AbstractField with DbType.String, NOT a CharField, so it fell into the same
        // else-branch. Its own doc claims fixed-width HH:mm:ss text "compares correctly with <, > and
        // BETWEEN" — which was false on this provider while the column was TEXT (Msg 306 on ORDER BY).
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.OpensAt))
            .Should().Be("NVARCHAR(MAX)",
                "a TimeOnly column was TEXT here too, so its documented ordering guarantee did not hold");

    // ---- the index-key branch, one entity per marking site

    [Fact]
    public void A_per_property_indexed_string_is_bounded()
        => TypeOf(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Code))
            .Should().Be("NVARCHAR(255)",
                "an index over NVARCHAR(MAX) raises the same Msg 1919 as one over TEXT");

    [Fact]
    public void A_composite_indexed_string_is_bounded()
        => TypeOf(typeof(CompositeIndexEntity), nameof(CompositeIndexEntity.Number))
            .Should().Be("NVARCHAR(255)",
                "the class-level attribute is the other of LoadIndexes' two marking sites");

    [Theory]
    [InlineData(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Untouched))]
    [InlineData(typeof(CompositeIndexEntity), nameof(CompositeIndexEntity.Untouched))]
    public void A_sibling_column_no_index_names_stays_unbounded(Type entity, string property)
        => TypeOf(entity, property)
            .Should().Be("NVARCHAR(MAX)",
                "only the columns an index actually names are narrowed — the bound is not per-entity");

    // ---- UNIQUE / PRIMARY KEY: index keys that IsIndexed does not see

    [Fact]
    public void A_unique_string_is_bounded_and_still_emits_unique()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));

        field.IsIndexed.Should().BeFalse(
            "LoadIndexes marks only [IndexedField]/[CompositeIndex] — this is exactly why IsInIndexKey exists");
        field.IsInIndexKey.Should().BeTrue("a UNIQUE column is an index key");

        var definition = Connector().FieldDefinition(field);
        definition.Should().Contain("NVARCHAR(255)",
            "an inline UNIQUE over NVARCHAR(MAX) raises Msg 1919 — it killed the whole CREATE TABLE");
        definition.Should().Contain("UNIQUE", "the constraint itself must survive the type change");
    }

    [Fact]
    public void A_primary_key_string_is_bounded_and_still_emits_primary_key()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.NaturalKey));

        field.IsIndexed.Should().BeFalse();
        field.IsInIndexKey.Should().BeTrue("a PRIMARY KEY column is an index key");

        var definition = Connector().FieldDefinition(field);
        definition.Should().Contain("NVARCHAR(255)");
        definition.Should().Contain("PRIMARY KEY");
    }

    // ---- the shared predicate itself

    [Fact]
    public void IsInIndexKey_is_the_union_of_the_three_flags()
    {
        var field = new StringField(typeof(PlainEntity).GetProperty(nameof(PlainEntity.Label))!, "Label");

        field.IsInIndexKey.Should().BeFalse("nothing set");

        field.IsIndexed = true;
        field.IsInIndexKey.Should().BeTrue("a declared index");
        field.IsIndexed = false;

        field.IsUnique = true;
        field.IsInIndexKey.Should().BeTrue("an inline UNIQUE constraint is an index too");
        field.IsUnique = false;

        field.IsPrimary = true;
        field.IsInIndexKey.Should().BeTrue("so is a PRIMARY KEY");
    }

    /// <summary>
    /// The bound is overridable, and the doc's claim that the real ceiling is SQL Server's key limit rather
    /// than 255 is only meaningful if a subclass can actually raise it.
    /// </summary>
    [Fact]
    public void The_indexed_bound_is_overridable()
    {
        var field = Field(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Code));

        new WiderConnector(new MSSqlSettings("localhost", "db", "user", "pass"))
            .ConvertType(field.Type, field)
            .Should().Be("NVARCHAR(450)", "450 chars = 900 bytes, the clustered-key ceiling");
    }

    private sealed class WiderConnector : MSSqlConnector
    {
        public WiderConnector(MSSqlSettings settings) : base(settings) { }
        protected override int IndexedStringColumnLength => 450;
    }

    /// <summary>
    /// <c>ConvertType</c> stays null-tolerant. Every other arm of the switch tests the field with an
    /// <c>is</c> pattern, which is simply false for null, and the pre-TASK-257 code returned <c>TEXT</c> for a
    /// null field rather than throwing — so a bare <c>!field.IsInIndexKey</c> would have made MSSql the only
    /// provider that NREs here, on public surface these very tests exercise. SqLite and PostgreSQL remain
    /// null-tolerant, so this keeps the four in step.
    /// </summary>
    [Fact]
    public void A_null_field_does_not_throw_and_yields_the_unbounded_type()
    {
        var act = () => Connector().ConvertType(DbType.String, null!);

        act.Should().NotThrow<NullReferenceException>(
            "a null field must not become an NRE — it did not before this change");
        Connector().ConvertType(DbType.String, null!).Should().Be("NVARCHAR(MAX)",
            "NVARCHAR(MAX) is the direct successor of the TEXT this used to return");
    }

    /// <summary>
    /// <c>ConvertType</c> is public surface a consumer may call with a hand-built field, so the hand-built
    /// path is kept — clearly labelled, because it is what cannot witness the dispatch fix on its own.
    /// </summary>
    [Theory]
    [InlineData(false, "NVARCHAR(MAX)")]
    [InlineData(true, "NVARCHAR(255)")]
    public void Public_surface_hand_built_field(bool indexed, string expected)
    {
        var field = new StringField(typeof(PlainEntity).GetProperty(nameof(PlainEntity.Label))!, "Label")
        {
            IsIndexed = indexed
        };

        Connector().ConvertType(DbType.String, field).Should().Be(expected);
    }
}
