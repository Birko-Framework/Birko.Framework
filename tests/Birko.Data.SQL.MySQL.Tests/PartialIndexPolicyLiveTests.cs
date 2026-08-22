using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-273 — the MySQL half of the predicate feature, which is a <b>policy</b> rather than an emitter:
/// MySQL supports no partial index at all (measured on 8.4.11: <c>CREATE INDEX … WHERE …</c> is
/// <c>ERROR 1064</c>), so each polarity has to be answered separately.
/// </summary>
/// <remarks>
/// <para>
/// <b>An <c>IS NOT NULL</c> term is dropped; an <c>IS NULL</c> term is refused.</b> The asymmetry is
/// measured, not aesthetic. MySQL treats NULLs as distinct, so for the first polarity the unfiltered index it
/// does emit enforces exactly the declared rule. For the second it does not: a full unique index rejects a
/// row whose duplicate is soft-deleted (measured on all four providers), so quietly dropping the term would
/// leave a <b>stricter</b> constraint than the one declared, refusing legitimate rows. That is the outcome
/// TASK-273's criterion 4 forbids, and it is why one polarity is silent and the other is loud.
/// </para>
/// <para>Gated on <c>BIRKO_MYSQL_HOST</c>; set <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.</para>
/// </remarks>
public class PartialIndexPolicyLiveTests : IDisposable
{
    private const string TableName = "MyPartialRows";
    private const string UniqueIndex = "ux_mypartial_extid";
    private const string LiveIndex = "ux_mypartial_live";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public PartialIndexPolicyLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);
    private static MySQLConnector NewConnector() => new(Settings());

    [Table(TableName)]
    [CompositeIndex(UniqueIndex, nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
        WhereNotNull = new[] { nameof(ExternalId) })]
    public class MyDroppableRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string? ExternalId { get; set; }
    }

    [Table(TableName)]
    [CompositeIndex(LiveIndex, nameof(TenantGuid), nameof(Number), IsUnique = true,
        WhereNull = new[] { nameof(DeletedAt) })]
    public class MyRefusedRow : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }

        [MaxLengthField(64)]
        public string? Number { get; set; }

        public DateTime? DeletedAt { get; set; }
    }

    private static void Exec(string sql)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static int? Insert(Guid tenant, string? value, string column = "ExternalId")
    {
        try
        {
            using var conn = new MySqlConnection(Settings().GetConnectionString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            // CreatedAt/UpdatedAt come from AbstractLogModel and are NOT NULL with no default, so omitting
            // them is MySQL error 1364 — a fixture mismatch that looks exactly like the defect under test.
            cmd.CommandText = $"INSERT INTO `{TableName}` (`Guid`, `CreatedAt`, `UpdatedAt`, `TenantGuid`, `{column}`) "
                            + "VALUES (UUID(), UTC_TIMESTAMP(), UTC_TIMESTAMP(), @t, @v)";
            cmd.Parameters.AddWithValue("@t", tenant.ToString());
            cmd.Parameters.AddWithValue("@v", (object?)value ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            return null;
        }
        catch (MySqlException ex)
        {
            return (int)ex.Number;
        }
    }

    private static bool IndexExists(string index)
    {
        using var conn = new MySqlConnection(Settings().GetConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() "
                        + "AND table_name = @t AND index_name = @i";
        cmd.Parameters.AddWithValue("@t", TableName);
        cmd.Parameters.AddWithValue("@i", index);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Host)) return;
        try { Exec($"DROP TABLE IF EXISTS `{TableName}`"); } catch { }
    }

    /// <summary>
    /// The capability's <c>false</c> side. Asserting it is what stops the flag being indistinguishable from
    /// an unconditional emit — a capability nothing measures can be deleted with no test noticing.
    /// </summary>
    [Fact]
    public void MySql_does_not_support_partial_indexes()
    {
        new MySQLConnector(new MySqlSettings("localhost", "db", "root", "p")).SupportsPartialIndexes
            .Should().BeFalse("CREATE INDEX … WHERE … is ERROR 1064 on MySQL 8.4.11");
    }

    /// <summary>
    /// Offline: the tail is dropped for <c>IS NOT NULL</c>, leaving the statement identical to the
    /// unfiltered one — and in particular NOT emitting a <c>WHERE</c> MySQL would reject.
    /// </summary>
    [Fact]
    public void The_mysql_emitter_drops_an_is_not_null_predicate()
    {
        var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ux_x", Unique = true };
        index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "TenantGuid", Order = 0 });
        index.Predicates.Add(new Birko.Data.SQL.Tables.IndexPredicate { ColumnName = "ExternalId" });

        var sql = new MySQLConnector(new MySqlSettings("localhost", "db", "root", "p")).CreateIndexSql("T", index);

        sql.Should().Be("CREATE UNIQUE INDEX `ux_x` ON `T` (TenantGuid)");
        sql.Should().NotContain("WHERE", "MySQL rejects the clause outright — ERROR 1064");
    }

    /// <summary>
    /// Offline: the emitter refuses an <c>IS NULL</c> term rather than silently producing a stricter index.
    /// The funnel refuses first; this is the backstop for a direct caller (§ TASK-137).
    /// </summary>
    [Fact]
    public void The_mysql_emitter_refuses_an_is_null_predicate()
    {
        var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ux_x", Unique = true };
        index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "TenantGuid", Order = 0 });
        index.Predicates.Add(new Birko.Data.SQL.Tables.IndexPredicate { ColumnName = "DeletedAt", RequireNull = true });

        Action act = () => new MySQLConnector(new MySqlSettings("localhost", "db", "root", "p")).CreateIndexSql("T", index);

        act.Should().Throw<InvalidOperationException>().WithMessage("*WhereNull*DeletedAt*");
    }

    /// <summary>
    /// Live, the droppable polarity: the index is really created (not merely "no exception"), and it enforces
    /// what the declaration asked for — many NULLs admitted, a duplicate non-NULL refused with 1062.
    /// </summary>
    [Fact]
    public void A_where_not_null_declaration_builds_and_behaves_on_mysql()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MyDroppableRow) });

        IndexExists(UniqueIndex).Should().BeTrue("the tail is dropped, so the statement MySQL gets is legal");
        connector.IndexCreationFailures.Should().BeEmpty();

        var tenant = Guid.NewGuid();
        Insert(tenant, null).Should().BeNull();
        Insert(tenant, null).Should().BeNull("MySQL treats NULLs as distinct — this is why dropping is safe here");
        Insert(tenant, "EXT-1").Should().BeNull();
        Insert(tenant, "EXT-1").Should().Be(1062, "uniqueness still enforced where the value is set");
    }

    /// <summary>
    /// Live, the refused polarity: schema-ensure <b>records</b> the failure rather than throwing (TASK-204),
    /// the index is absent, and — the part that matters — the entity is still fully usable. A refusal that
    /// bricked the store would be worse than the defect.
    /// </summary>
    [Fact]
    public void A_where_null_declaration_is_recorded_as_a_failure_and_the_entity_still_works()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");

        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MyRefusedRow) });

        IndexExists(LiveIndex).Should().BeFalse("the predicate cannot be expressed and must not be dropped");
        connector.IndexCreationFailures.Should().ContainSingle()
            .Which.IndexName.Should().Be(LiveIndex);

        var tenant = Guid.NewGuid();
        Insert(tenant, "DOC-1", "Number").Should().BeNull("the table itself must remain usable");
        Insert(tenant, "DOC-1", "Number").Should().BeNull(
            "and without the index there is no constraint — which is the honest state, not a silent stricter one");
    }

    /// <summary>
    /// The explicit door still throws, per TASK-204's split: schema-ensure degrades, a caller naming this
    /// index now fails loudly. The message must name the two ways out, or the guard gets reached around.
    /// </summary>
    [Fact]
    public void An_explicit_create_indexes_call_throws_for_a_where_null_declaration()
    {
        if (!RequireServer()) return;
        Exec($"DROP TABLE IF EXISTS `{TableName}`");
        var connector = NewConnector();
        connector.CreateTable(new[] { typeof(MyRefusedRow) });

        var index = Birko.Data.SQL.DataBase.LoadTable(typeof(MyRefusedRow)).Indexes![LiveIndex];

        Action act = () => connector.CreateIndexes(TableName, new[] { index });

        var refusal = act.Should().Throw<InvalidOperationException>().Which;
        refusal.Message.Should().Contain("ERROR 1064");
        refusal.Message.Should().Contain("remove the WhereNull declaration");
    }
}
