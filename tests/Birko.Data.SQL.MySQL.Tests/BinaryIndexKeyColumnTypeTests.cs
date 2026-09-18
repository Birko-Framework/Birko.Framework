using System;
using System.Data;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-266 — what a <c>byte[]</c> column declares on MySQL, and what changes when an index key names it.
/// The binary half of the hole TASK-257 closed for strings on SQL Server and TASK-265 closed for strings
/// here.
///
/// <para>
/// <b>What was wrong.</b> <c>ConvertType</c> mapped <c>DbType.Binary</c> to <c>LONGBLOB</c>
/// unconditionally, and MySQL cannot use a BLOB/TEXT column in a key without a key length. Measured on
/// live MySQL 8.4.11: both an inline <c>LONGBLOB UNIQUE</c> and <c>CREATE INDEX</c> over a <c>LONGBLOB</c>
/// raise <b>ERROR 1170</b> — so a <c>[UniqueField] byte[]</c> entity's table could not be created at all,
/// and an <c>[IndexedField]</c> one lost its index into <c>IndexCreationFailures</c>.
/// </para>
/// <para>
/// <b>Why 255 when MySQL's own ceiling is far higher.</b> Measured on 8.4.11 (utf8mb4, <c>dynamic</c> row
/// format): <c>VARBINARY(3072) UNIQUE</c> is accepted and <c>VARBINARY(3073)</c> raises <b>ERROR 1071</b>,
/// so the InnoDB key limit is 3072 bytes — nearly double SQL Server's 1700. 255 is chosen to agree with
/// SQL Server rather than to sit near this server's limit: the same model runs on both, so a width that
/// indexes on one must index on the other. The same reasoning TASK-257 recorded for the string knob.
/// </para>
/// <para>
/// Every case goes through <c>DataBase.LoadTable</c>, never a hand-built field — a hand-built
/// <c>BinaryField</c> survives a revert of the <c>CreateAbstractField</c> pass-through and so cannot
/// witness half the fix.
/// </para>
/// <para>No live server required; <c>ConvertType</c> is pure.</para>
/// </summary>
public class BinaryIndexKeyColumnTypeTests
{
    // ---- probe entities. Distinct CLR types and table names: DataBase's table cache is static per process.

    [Table("MyBinPlain")]
    public class PlainEntity : AbstractLogModel
    {
        public byte[] Blob { get; set; } = Array.Empty<byte>();

        [MaxLengthField(32)]
        public byte[] Sha256 { get; set; } = Array.Empty<byte>();
    }

    [Table("MyBinPerProp")]
    public class PerPropertyIndexEntity : AbstractLogModel
    {
        [IndexedField("ix_mybinperprop_fp")]
        public byte[] Fingerprint { get; set; } = Array.Empty<byte>();

        public byte[] Untouched { get; set; } = Array.Empty<byte>();
    }

    [Table("MyBinComposite")]
    [CompositeIndex("ux_mybincomposite_digest", nameof(Digest), IsUnique = true)]
    public class CompositeIndexEntity : AbstractLogModel
    {
        public byte[] Digest { get; set; } = Array.Empty<byte>();

        public byte[] Untouched { get; set; } = Array.Empty<byte>();
    }

    [Table("MyBinConstraints")]
    public class ConstraintEntity : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        public byte[] Sku { get; set; } = Array.Empty<byte>();

        [PrimaryField]
        public byte[] NaturalKey { get; set; } = Array.Empty<byte>();
    }

    [Table("MyBinBoundedKey")]
    public class BoundedKeyEntity : AbstractLogModel
    {
        [UniqueField]
        [RequiredField]
        [MaxLengthField(16)]
        public byte[] Uuid { get; set; } = Array.Empty<byte>();
    }

    private static MySQLConnector Connector()
        => new(new MySqlSettings("localhost", "db", "user", "pass"));

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
    public void An_unindexed_binary_column_is_still_longblob()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Blob))
            .Should().Be("LONGBLOB",
                "a bounded default would start refusing blobs that write fine today");

    [Fact]
    public void An_unindexed_binary_column_on_an_indexed_entity_is_untouched()
        => TypeOf(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Untouched))
            .Should().Be("LONGBLOB", "only the column the index names is bounded");

    // ---- an index key is bounded, by all three routes into IsInIndexKey

    [Fact]
    public void A_per_property_indexed_binary_column_is_bounded()
        => TypeOf(typeof(PerPropertyIndexEntity), nameof(PerPropertyIndexEntity.Fingerprint))
            .Should().Be("VARBINARY(255)", "CREATE INDEX over a LONGBLOB is ERROR 1170");

    [Fact]
    public void A_composite_indexed_binary_column_is_bounded()
        => TypeOf(typeof(CompositeIndexEntity), nameof(CompositeIndexEntity.Digest))
            .Should().Be("VARBINARY(255)",
                "the class-level marking site must mark it too — TASK-248's suite covered only one of the "
                + "two and a revert of the other failed 0 tests");

    [Fact]
    public void A_unique_binary_column_is_bounded()
        => TypeOf(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku))
            .Should().Be("VARBINARY(255)", "LONGBLOB UNIQUE is ERROR 1170 at CREATE TABLE");

    [Fact]
    public void A_primary_key_binary_column_is_bounded()
        => TypeOf(typeof(ConstraintEntity), nameof(ConstraintEntity.NaturalKey))
            .Should().Be("VARBINARY(255)");

    // ---- a declared length always wins

    [Fact]
    public void A_declared_length_is_honoured_on_an_unindexed_column()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Sha256))
            .Should().Be("VARBINARY(32)",
                "[MaxLengthField] on a byte[] was silently dropped before TASK-266");

    [Fact]
    public void A_declared_length_beats_the_indexed_default()
        => TypeOf(typeof(BoundedKeyEntity), nameof(BoundedKeyEntity.Uuid))
            .Should().Be("VARBINARY(16)");

    // ---- the gate: DbType.Object shares this switch arm

    [Fact]
    public void A_DbType_Object_field_never_takes_a_length()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));

        // null! rather than null: the parameter is non-nullable but the arm handles a null field, and
        // CLAUDE.md forbids a CS8625 in new code.
        Connector().ConvertType(DbType.Object, null!)
            .Should().Be("LONGBLOB", "no field at all must not NRE and must not be bounded");
        Connector().ConvertType(DbType.Object, field)
            .Should().Be("VARBINARY(255)",
                "the gate is the field's runtime type, not the DbType — this pins which of the two decides");
    }

    // ---- the knob

    private sealed class WiderConnector : MySQLConnector
    {
        public WiderConnector() : base(new MySqlSettings("localhost", "db", "user", "pass")) { }
        protected override int IndexedBinaryColumnLength => 3072;
    }

    /// <summary>
    /// 255 is a cross-provider agreement, not this server's ceiling — measured, InnoDB allows 3072 bytes.
    /// The override is what makes that reachable.
    /// </summary>
    [Fact]
    public void The_indexed_binary_length_is_overridable()
    {
        var field = Field(typeof(ConstraintEntity), nameof(ConstraintEntity.Sku));

        new WiderConnector().ConvertType(field.Type, field).Should().Be("VARBINARY(3072)");
    }
}
