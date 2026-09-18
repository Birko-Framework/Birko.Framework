using System;
using System.Data.Common;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// TASK-274 — <c>IIndexBuilder.Sparse()</c> on the SQL lane: honoured for one column, refused for a compound
/// index, and no longer silently discarded.
/// </summary>
/// <remarks>
/// <para>
/// It used to be <c>=> this</c>. For a UNIQUE index that is not a lost optimisation but a <b>stricter</b>
/// constraint than declared — it rejects rows the declaration permits — which is the same class of harm
/// TASK-273 measured. It is now expressed through that task's <c>WhereNotNull</c> predicate.
/// </para>
/// <para>
/// <b>These tests take the connector path.</b> TASK-246 stayed green for a release because every test in
/// this project constructed the builder with <c>connector == null</c> and exercised a raw-SQL fallback
/// nothing shipped; TASK-247 then deleted that fallback and made the connector required. This suite drives a
/// real on-disk SQLite database through <c>SqlMigrationRunner</c>'s own builder so the assertion is about the
/// code that ships.
/// </para>
/// </remarks>
public class SparseIndexBuilderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SparseIndexBuilderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"birko-sparse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "sparse.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private SqLiteSettings Settings() => new(_dir, Path.GetFileName(_dbPath));

    private SqLiteConnector Connector() => new(Settings());

    private (DbConnection Connection, ISchemaBuilder Schema) OpenSchema()
    {
        var connector = Connector();
        connector.CreateTable("SparseRows", new[] { "Guid TEXT", "Code TEXT", "Branch TEXT" });

        var connection = new SqliteConnection(Settings().GetConnectionString());
        connection.Open();
        return (connection, new SqlSchemaBuilder(connection, null, connector));
    }

    private string IndexSql(string index)
    {
        using var connection = new SqliteConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = @i";
        command.Parameters.AddWithValue("@i", index);
        return command.ExecuteScalar() as string ?? string.Empty;
    }

    /// <summary>
    /// A single-column sparse index becomes a partial index — which is exactly what "skips rows without the
    /// indexed field" means when there is one field.
    /// </summary>
    [Fact]
    public void A_single_column_sparse_index_becomes_a_partial_index()
    {
        var (connection, schema) = OpenSchema();
        using var _ = connection;

        schema.CreateIndex("SparseRows", "ux_sparse_code").WithField("Code").Unique().Sparse().Build();

        IndexSql("ux_sparse_code").Should().Contain("UNIQUE")
            .And.Contain("WHERE Code IS NOT NULL",
                "the sparseness is the predicate — dropping it would make the constraint stricter than "
              + "declared, not merely less selective");
    }

    /// <summary>
    /// Without <c>Sparse()</c> the index is unchanged — the boundary of the feature, and the pin that stops
    /// a predicate appearing where nobody asked for one.
    /// </summary>
    [Fact]
    public void An_ordinary_index_gets_no_predicate()
    {
        var (connection, schema) = OpenSchema();
        using var _ = connection;

        schema.CreateIndex("SparseRows", "ix_plain_code").WithField("Code").Build();

        IndexSql("ix_plain_code").Should().NotBeEmpty().And.NotContain("WHERE");
    }

    /// <summary>
    /// A compound sparse index is <b>refused</b>: Mongo includes a document when ANY key is present, a SQL
    /// partial index requires ALL of them, and <c>IIndexBuilder</c> does not say which <c>Sparse()</c> means.
    /// Picking one silently is what this whole family of defects is made of.
    /// </summary>
    [Fact]
    public void A_compound_sparse_index_is_refused_rather_than_given_one_of_two_readings()
    {
        var (connection, schema) = OpenSchema();
        using var _ = connection;

        Action act = () => schema.CreateIndex("SparseRows", "ux_sparse_compound")
            .WithField("Code").WithField("Branch").Unique().Sparse().Build();

        act.Should().Throw<NotSupportedException>()
            .Which.Message.Should().Contain("ANY key is present").And.Contain("WhereNotNull");

        IndexSql("ux_sparse_compound").Should().BeEmpty("nothing may be created when the declaration is refused");
    }

    /// <summary>
    /// <c>WithProperty</c> is refused rather than swallowed — the SQL emitter has nothing for it to reach,
    /// and ElasticSearch and RavenDB DO honour <c>IndexDefinition.Properties</c>, so silently ignoring it
    /// here would make one lane's contract look like another's.
    /// </summary>
    [Fact]
    public void An_index_property_is_refused()
    {
        var (connection, schema) = OpenSchema();
        using var _ = connection;

        Action act = () => schema.CreateIndex("SparseRows", "ix_prop")
            .WithField("Code").WithProperty("fillfactor", 70).Build();

        act.Should().Throw<NotSupportedException>().Which.Message.Should().Contain("fillfactor");
    }
}
