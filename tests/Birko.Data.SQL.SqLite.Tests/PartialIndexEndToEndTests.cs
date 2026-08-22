using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-273 — partial unique indexes on SQLite, end to end against a real on-disk database.
/// </summary>
/// <remarks>
/// This is the provider every consumer's own test suite runs on, so it is where a regression would be seen
/// first — and equally where a green run proves least about the other three (SQLite treats NULLs as distinct
/// and is case-insensitive about identifiers, so neither the defect this feature fixes nor the folding hazard
/// exists here). Ungated: no server, so it runs everywhere.
/// </remarks>
public class PartialIndexEndToEndTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "birko-task273-" + Guid.NewGuid().ToString("N"));

    public PartialIndexEndToEndTests() => Directory.CreateDirectory(_root);

    [Table("SqPartialRows")]
    [CompositeIndex("ux_sqpartial_extid", nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
        WhereNotNull = new[] { nameof(ExternalId) })]
    [CompositeIndex("ux_sqpartial_live", nameof(TenantGuid), nameof(Number), IsUnique = true,
        WhereNull = new[] { nameof(DeletedAt) })]
    public class SqPartialRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? ExternalId { get; set; }
        public string? Number { get; set; }
        public DateTime? DeletedAt { get; set; }
    }

    private SqLiteSettings Settings() => new(_root, "partial.db");

    private SqLiteConnector NewConnector() => new(Settings());

    private string ConnectionString() => Settings().GetConnectionString();

    private string? IndexSql(string index)
    {
        using var conn = new SqliteConnection(ConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @i";
        cmd.Parameters.AddWithValue("@i", index);
        return cmd.ExecuteScalar() as string;
    }

    private int? Insert(Guid tenant, string? externalId, string? number, DateTime? deletedAt)
    {
        try
        {
            using var conn = new SqliteConnection(ConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO SqPartialRows (Guid, CreatedAt, UpdatedAt, TenantGuid, ExternalId, Number, DeletedAt) "
                            + "VALUES (@g, @c, @c, @t, @e, @n, @d)";
            cmd.Parameters.AddWithValue("@g", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@t", tenant.ToString());
            cmd.Parameters.AddWithValue("@e", (object?)externalId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@n", (object?)number ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", (object?)deletedAt ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (SqliteException ex)
        {
            return ex.SqliteExtendedErrorCode;
        }
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Both_predicate_polarities_reach_the_created_index()
    {
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(SqPartialRow) });
        connector.IndexCreationFailures.Should().BeEmpty();

        IndexSql("ux_sqpartial_extid").Should().Contain("WHERE ExternalId IS NOT NULL");
        IndexSql("ux_sqpartial_live").Should().Contain("WHERE DeletedAt IS NULL");
    }

    /// <summary>
    /// 2067 is <c>SQLITE_CONSTRAINT_UNIQUE</c>. Both directions, because a one-directional assertion cannot
    /// tell a working partial index from an absent one.
    /// </summary>
    [Fact]
    public void A_where_not_null_index_admits_many_nulls_and_rejects_a_set_duplicate()
    {
        NewConnector().CreateTable(new[] { typeof(SqPartialRow) });
        var tenant = Guid.NewGuid();

        Insert(tenant, null, null, null).Should().BeNull();
        Insert(tenant, null, null, null).Should().BeNull();
        Insert(tenant, "EXT-1", null, null).Should().BeNull();
        Insert(tenant, "EXT-1", null, null).Should().Be(2067);
    }

    [Fact]
    public void A_where_null_index_is_unique_among_live_rows_only()
    {
        NewConnector().CreateTable(new[] { typeof(SqPartialRow) });
        var tenant = Guid.NewGuid();

        Insert(tenant, null, "DOC-1", new DateTime(2020, 1, 1)).Should().BeNull("soft-deleted holder");
        Insert(tenant, null, "DOC-1", null).Should().BeNull("the number is reusable once the holder is deleted");
        Insert(tenant, null, "DOC-1", null).Should().Be(2067, "two live rows may not share it");
    }

    /// <summary>
    /// Criterion 7 at the metadata layer, on the provider whose DDL is easiest to inspect: an ordinary index
    /// gets no <c>WHERE</c> at all.
    /// </summary>
    [Fact]
    public void An_ordinary_index_carries_no_predicate()
    {
        NewConnector().CreateTable(new[] { typeof(SqPlainRow) });

        IndexSql("ux_sqplain").Should().NotContain("WHERE");
    }

    [Table("SqPlainRows")]
    [CompositeIndex("ux_sqplain", nameof(TenantGuid), nameof(Number), IsUnique = true)]
    public class SqPlainRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? Number { get; set; }
    }
}
