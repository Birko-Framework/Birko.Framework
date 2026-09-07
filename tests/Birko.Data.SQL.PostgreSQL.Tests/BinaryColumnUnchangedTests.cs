using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// TASK-266 — <b>the false side of the switch.</b> The binary index-key bounding is deliberately scoped to
/// SQL Server and MySQL, whose key types cannot be unbounded blobs. PostgreSQL's <c>BYTEA</c> has no
/// declarable length and indexes perfectly well as it is, so nothing here changes.
///
/// <para>
/// Asserting that matters as much as asserting the fix: without it the change is indistinguishable from a
/// blanket one, and a blanket one would be wrong — bounding <c>BYTEA</c> is not even expressible.
/// § TASK-278 records the same discipline for <c>RequiresOrderByForPaging</c>, where forcing the
/// capability true left the affected provider green and only the unaffected ones caught it. It is also
/// why TASK-266 does <b>not</b> refuse an unbounded binary index key at load time: the declaration is
/// legal here, and a framework-wide refusal would break a working entity on this provider to fix another
/// (§ TASK-248's veto).
/// </para>
/// <para>No live server required; <c>ConvertType</c> is pure.</para>
/// </summary>
public class BinaryColumnUnchangedTests
{
    [Table("PgBinPlain")]
    public class PlainEntity : AbstractLogModel
    {
        public byte[] Blob { get; set; } = Array.Empty<byte>();

        [MaxLengthField(32)]
        public byte[] Sha256 { get; set; } = Array.Empty<byte>();
    }

    [Table("PgBinConstraints")]
    [CompositeIndex("ux_pgbin_digest", nameof(Digest), IsUnique = true)]
    public class ConstraintEntity : AbstractLogModel
    {
        public byte[] Digest { get; set; } = Array.Empty<byte>();

        [UniqueField]
        [RequiredField]
        public byte[] Sku { get; set; } = Array.Empty<byte>();

        [PrimaryField]
        public byte[] NaturalKey { get; set; } = Array.Empty<byte>();
    }

    private static PostgreSQLConnector Connector()
        => new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    private static string TypeOf(Type entity, string property)
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(entity);
        var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
        field.Should().NotBeNull($"'{property}' must map to a column at all");
        return Connector().ConvertType(field!.Type, field!);
    }

    [Fact]
    public void An_unindexed_binary_column_is_bytea()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Blob)).Should().Be("BYTEA");

    /// <summary>
    /// A declared length is accepted by the field and correctly <b>ignored</b> here — <c>BYTEA</c> takes
    /// no length, so honouring it would be inventing syntax PostgreSQL does not have.
    /// </summary>
    [Fact]
    public void A_declared_length_does_not_change_the_column_here()
        => TypeOf(typeof(PlainEntity), nameof(PlainEntity.Sha256)).Should().Be("BYTEA",
            "BYTEA has no length form; the field carries the width for the providers that need it");

    [Theory]
    [InlineData(nameof(ConstraintEntity.Digest))]
    [InlineData(nameof(ConstraintEntity.Sku))]
    [InlineData(nameof(ConstraintEntity.NaturalKey))]
    public void An_index_key_binary_column_is_still_bytea(string property)
        => TypeOf(typeof(ConstraintEntity), property).Should().Be("BYTEA",
            "an unbounded binary unique key is legal on PostgreSQL, which is why TASK-266 absorbs the "
            + "limit at the two providers that have one rather than refusing the declaration");
}
