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
/// TASK-266 — what a <c>byte[]</c> column declares on SQL Server, and what changes when an index key names
/// it. The binary sibling of <see cref="IndexedStringColumnTypeTests"/>, which TASK-257 deliberately left
/// alone because every one of its acceptance criteria said *string*.
///
/// <para>
/// <b>What was wrong.</b> <c>ConvertType</c> mapped <c>DbType.Binary</c> to <c>VARBINARY(MAX)</c>
/// unconditionally (CR-M137, to avoid a bare <c>BINARY</c> defaulting to <c>BINARY(1)</c>), and a MAX type
/// cannot be an index key. Measured against live SQL Server 2022 (16.0.4265.3): an inline
/// <c>UNIQUE</c> over <c>VARBINARY(MAX)</c> raises <b>Msg 1919 + Msg 1750</b> — and, unlike most DDL
/// errors, is <b>not catchable by <c>TRY/CATCH</c></b>, so the batch aborts and the whole
/// <c>CREATE TABLE</c> fails. <c>CREATE INDEX</c> over one raises the same Msg 1919. So a
/// <c>[UniqueField] byte[] Hash</c> entity had no table at all, and an <c>[IndexedField]</c> one lost its
/// index into <c>IndexCreationFailures</c>, which nothing subscribes to.
/// </para>
/// <para>
/// <b>Why bounding rather than refusing the declaration.</b> Unbounded binary as a unique key is perfectly
/// legal on PostgreSQL (<c>BYTEA</c>) and SQLite (<c>BLOB</c>), so a load-time refusal would convert a
/// working declaration into a start-up failure on two providers to fix two others — § TASK-248's veto,
/// which is exactly why that task absorbed MySQL's own limit at MySQL. The provider's limit is absorbed at
/// the provider.
/// </para>
/// <para>
/// Every case goes through <c>DataBase.LoadTable</c> rather than constructing a field by hand: a hand-built
/// <c>BinaryField</c> survives a revert of the <c>CreateAbstractField</c> pass-through, so such a test
/// could not witness half of this fix (the same reason <see cref="IndexedStringColumnTypeTests"/> gives).
/// </para>
/// <para>No live server required; <c>ConvertType</c> is pure. The live half is
/// <c>BinaryAndWideCompositeIndexLiveTests</c>.</para>
/// </summary>
public class BinaryIndexKeyColumnTypeTests
{
    // ---- probe entities. Distinct CLR types and table names: DataBase's table cache is static per process.

    /// <summary>Nothing declared about the binary column — the default shape.</summary>
    [Table("MsBinPlain")]
    public class PlainEntity : AbstractLogModel
    {
        public byte[] Blob { get; set; } = Array.Empty<byte>();

        [MaxLengthField(32)]
        public byte[] Sha256 { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Per-property index — <c>DataBase_Table.cs</c>'s first <c>IsIndexed</c> marking site.</summary>
    [Table("MsBinPerProp")]
    public class PerPropertyIndexEntity : AbstractLogModel
    {
        [IndexedField("ix_msbinperprop_fp")]
        public byte[] Fingerprint { get; set; } = Array.Empty<byte>();

        public byte[] Untouched { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Class-level composite — the second marking site, which an unqualified grep misses.</summary>
    [Table("MsBinComposite")]
    [CompositeIndex("ux_msbincomposite_digest", nameof(Digest), IsUnique = true)]
    public class CompositeIndexEntity : AbstractLogModel
    {
        public byte[] Digest { get; set; } = Array.Empty<byte>();

        public byte[] Untouched { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// <c>UNIQUE</c> and <c>PRIMARY KEY</c> are index keys that <c>LoadIndexes</c> never marks — so this
    /// entity is what makes <c>IsInIndexKey</c> load-bearing rather than <c>IsIndexed</c>.
    /// </summary>
    [Table("MsBinConstraints")]
    public class ConstraintEntity : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public byte[] Sku { get; set; } = Array.Empty<byte>();

        [PrimaryField]
        public byte[] NaturalKey { get; set; } = Array.Empty<byte>();
    }

    /// <summary>A declared length wins over the indexed default — the hash case the task argued for.</summary>
    [Table("MsBinBoundedKey")]
    public class BoundedKeyEntity : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        [MaxLengthField(16)]
        public byte[] Uuid { get; set; } = Array.Empty<byte>();
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
    {
        var field = Field(entity, property);
        return Connector().ConvertType(field.Type, field);
    }

    // ---- the unindexed default is unchanged

    [Fact]
    public void An_unindexed_binary_column_is_still_varbinary_max()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Blob))
            .Should().Be("VARBINARY(MAX)",
                "CR-M137's reason still holds — a bare BINARY defaults to BINARY(1) — and a bounded default "
                + "would start refusing blobs that write fine today");

    [Fact]
    public void An_unindexed_binary_column_on_an_indexed_entity_is_untouched()
        => TypeOf(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Untouched))
            .Should().Be("VARBINARY(MAX)", "only the column the index names is bounded");

    // ---- an index key is bounded, by all three routes into IsInIndexKey

    [Fact]
    public void A_per_property_indexed_binary_column_is_bounded()
        => TypeOf(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Fingerprint))
            .Should().Be("VARBINARY(255)",
                "an index over VARBINARY(MAX) raises Msg 1919, so the index could never be built");

    [Fact]
    public void A_composite_indexed_binary_column_is_bounded()
        => TypeOf(typeof(CompositeIndexEntity), nameof(CompositeIndexEntity.Digest))
            .Should().Be("VARBINARY(255)",
                "the class-level marking site must mark it too — TASK-248 shipped a suite that only "
                + "covered one of the two and a revert of the other failed 0 tests");

    [Fact]
    public void A_unique_binary_column_is_bounded()
        => TypeOf(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku))
            .Should().Be("VARBINARY(255)",
                "FieldDefinition emits UNIQUE as an inline column constraint, and Msg 1919 there is not "
                + "catchable — it takes down the whole CREATE TABLE, not just the constraint");

    [Fact]
    public void A_primary_key_binary_column_is_bounded()
        => TypeOf(typeof(ConstraintEntity), nameof(ConstraintEntity.NaturalKey))
            .Should().Be("VARBINARY(255)");

    // ---- a declared length always wins

    [Fact]
    public void A_declared_length_is_honoured_on_an_unindexed_column()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Sha256))
            .Should().Be("VARBINARY(32)",
                "[MaxLengthField] on a byte[] was silently dropped before TASK-266 — BinaryField had no "
                + "length at all, so 'declare a length' was an escape hatch that did not open");

    [Fact]
    public void A_declared_length_beats_the_indexed_default()
        => TypeOf(typeof(BoundedKeyEntity), nameof(BoundedKeyEntity.Uuid))
            .Should().Be("VARBINARY(16)",
                "a hash or UUID key has a natural exact width, and 255 would be arbitrary padding");

    // ---- the gate: DbType.Object shares this switch arm on all four connectors

    /// <summary>
    /// <c>DbType.Object</c> falls into the same <c>case</c> as <c>DbType.Binary</c>, so the branch is gated
    /// on <c>field is BinaryField</c>. A serialized object has no byte width to declare, and a length
    /// applied to one would silently truncate it.
    /// </summary>
    [Fact]
    public void A_DbType_Object_field_never_takes_a_length()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));

        // Same field, asked as DbType.Object: the arm is shared, the gate is the field's runtime type.
        // null! rather than null: the parameter is non-nullable but the arm explicitly handles a null
        // field (see its `field == null ||` guard), and CLAUDE.md forbids a CS8625 in new code.
        Connector().ConvertType(DbType.Object, null!)
            .Should().Be("VARBINARY(MAX)", "no field at all must not NRE and must not be bounded");
        Connector().ConvertType(DbType.Object, field)
            .Should().Be("VARBINARY(255)",
                "a real BinaryField that IS an index key is still bounded, because the gate is the field "
                + "type rather than the DbType — this pins which of the two decides");
    }

    // ---- the knob

    private sealed class WiderConnector : MSSqlConnector
    {
        public WiderConnector() : base(new MSSqlSettings("localhost", "db", "user", "pass")) { }
        protected override int IndexedBinaryColumnLength => 900;
    }

    /// <summary>
    /// 255 is a cross-provider agreement, not SQL Server's ceiling: measured on 16.0.4265.3,
    /// <c>VARBINARY(901) UNIQUE</c> is accepted, the real limit being the 1700-byte nonclustered key. The
    /// override exists so that is reachable, and it has a test because "the real ceiling is the key limit,
    /// not this number" is otherwise a comment nothing enforces.
    /// </summary>
    [Fact]
    public void The_indexed_binary_length_is_overridable()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));

        new WiderConnector().ConvertType(field.Type, field).Should().Be("VARBINARY(900)");
    }

    /// <summary>
    /// The string knob is untouched, so a project overriding one does not silently move the other. They
    /// share a number and not a unit — bytes for binary, characters for <c>NVARCHAR</c>.
    /// </summary>
    [Fact]
    public void The_string_length_and_the_binary_length_are_separate_knobs()
    {
        var binary = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));
        var wider = new WiderConnector();

        wider.ConvertType(binary.Type, binary).Should().Be("VARBINARY(900)");
        wider.ConvertType(DbType.String, Field(typeof(IndexedStringColumnTypeTests.ConstraintEntity),
                nameof(IndexedStringColumnTypeTests.ConstraintEntity.Sku)))
            .Should().Be("NVARCHAR(255)", "overriding the binary knob must not move the string one");
    }
}
