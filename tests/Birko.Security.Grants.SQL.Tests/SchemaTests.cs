using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.SQL;
using Birko.Security.Grants.SQL.Mappings;
using Birko.Security.Grants.SQL.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Security.Grants.SQL.Tests;

/// <summary>Builds a fresh SQLite database per test and the two grant stores on it.</summary>
public sealed class SqliteGrantsFixture : IDisposable
{
    private static readonly object MappingGate = new();
    private static bool _mapped;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"birko-grants-sql-{Guid.NewGuid():N}");
    public SqLiteSettings Settings { get; }
    public SqlResourceGrantStore<SqLiteConnector> Grants { get; } = new();
    public SqlInvitationStore<SqLiteConnector> Invitations { get; } = new();

    /// <summary>The mapping registry writes process-wide state (table names, field flags); apply it once per process.</summary>
    public static void EnsureMapped()
    {
        lock (MappingGate)
        {
            if (!_mapped)
            {
                GrantSqlMappings.Register(new ModelMapRegistry()).ApplyToDatabase();
                _mapped = true;
            }
        }
    }

    public SqliteGrantsFixture()
    {
        EnsureMapped();
        Directory.CreateDirectory(Root);
        Settings = new SqLiteSettings(Root, "grants.db");
        Grants.SetSettings(Settings);
        Invitations.SetSettings(Settings);
    }

    public List<(string Name, string Type, bool NotNull, bool Pk)> Columns(string table)
    {
        using var connection = new SqliteConnection(Settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        var columns = new List<(string, string, bool, bool)>();
        while (reader.Read())
            columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3) == 1, reader.GetInt32(5) > 0));
        return columns;
    }

    public List<string> IndexSql(string table)
    {
        using var connection = new SqliteConnection(Settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(sql, name) FROM sqlite_master WHERE type = 'index' AND tbl_name = @t";
        command.Parameters.AddWithValue("@t", table);
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    public void Dispose()
    {
        // Only this database's pool: a process-wide ClearAllPools breaks parallel siblings (rule 61).
        using (var probe = new SqliteConnection(Settings.GetConnectionString()))
            SqliteConnection.ClearPool(probe);
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}

public class SchemaTests : IDisposable
{
    private readonly SqliteGrantsFixture _f = new();

    public void Dispose() => _f.Dispose();

    [Fact]
    public async Task ResourceGrant_GetsATable_WithEveryProperty()
    {
        await _f.Grants.CreateAsync(new ResourceGrant { UserGuid = Guid.NewGuid(), ResourceType = "doc", Role = "owner" });

        var columns = _f.Columns(ResourceGrantMapping.Table).Select(c => c.Name).ToList();

        columns.Should().BeEquivalentTo(typeof(ResourceGrant).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task Invitation_GetsATable_WithEveryProperty()
    {
        await _f.Invitations.CreateAsync(new Invitation { Email = "a@example.com", ResourceType = "doc", Role = "viewer", TokenHash = "H" });

        var columns = _f.Columns(InvitationMapping.Table).Select(c => c.Name).ToList();

        columns.Should().BeEquivalentTo(typeof(Invitation).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task RequiredColumns_AreNotNull_AndNullableOnesNullable()
    {
        await _f.Invitations.InitAsync();

        var columns = _f.Columns(InvitationMapping.Table).ToDictionary(c => c.Name);

        columns["Email"].NotNull.Should().BeTrue();
        columns["TokenHash"].NotNull.Should().BeTrue();
        columns["ResourceId"].NotNull.Should().BeFalse();
        columns["RedeemedAt"].NotNull.Should().BeFalse();
        columns["Guid"].Pk.Should().BeTrue();
    }

    [Fact]
    public async Task AGrant_ReadsBack_FieldForField()
    {
        var full = new ResourceGrant
        {
            UserGuid = Guid.NewGuid(), ResourceType = "doc", ResourceId = Guid.NewGuid(), Role = "owner",
            TenantGuid = Guid.NewGuid(), GrantedBy = Guid.NewGuid(), CreatedAt = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc),
        };
        var typeWide = new ResourceGrant { UserGuid = Guid.NewGuid(), ResourceType = "doc", Role = "viewer", CreatedAt = full.CreatedAt };
        await _f.Grants.CreateAsync(full);
        await _f.Grants.CreateAsync(typeWide);

        (await _f.Grants.ReadAsync(full.Guid!.Value)).Should().BeEquivalentTo(full);
        (await _f.Grants.ReadAsync(typeWide.Guid!.Value)).Should().BeEquivalentTo(typeWide);
    }

    [Fact]
    public async Task AnInvitation_ReadsBack_FieldForField_NullsIncluded()
    {
        var at = new DateTime(2026, 10, 14, 9, 30, 0, DateTimeKind.Utc);
        var pending = new Invitation
        {
            Email = "a@example.com", ResourceType = "doc", ResourceId = Guid.NewGuid(), Role = "viewer", TokenHash = "H1",
            InvitedBy = Guid.NewGuid(), CreatedAt = at, ExpiresAt = at.AddDays(7),
        };
        var redeemed = new Invitation
        {
            Email = "b@example.com", ResourceType = "doc", Role = "editor", TenantGuid = Guid.NewGuid(), TokenHash = "H2",
            InvitedBy = Guid.NewGuid(), CreatedAt = at, ExpiresAt = at.AddDays(7), RedeemedAt = at.AddHours(1),
            RedeemedBy = Guid.NewGuid(), RevokedAt = null,
        };
        await _f.Invitations.CreateAsync(pending);
        await _f.Invitations.CreateAsync(redeemed);

        (await _f.Invitations.ReadAsync(pending.Guid!.Value)).Should().BeEquivalentTo(pending);
        (await _f.Invitations.ReadAsync(redeemed.Guid!.Value)).Should().BeEquivalentTo(redeemed);
    }

    [Fact]
    public async Task TheTokenHash_IsUnique_InTheDatabase()
    {
        await _f.Invitations.CreateAsync(new Invitation { Email = "a@example.com", ResourceType = "doc", Role = "viewer", TokenHash = "SAME" });

        var duplicate = () => _f.Invitations.CreateAsync(new Invitation { Email = "b@example.com", ResourceType = "doc", Role = "viewer", TokenHash = "SAME" });

        await duplicate.Should().ThrowAsync<Exception>();
        _f.IndexSql(InvitationMapping.Table).Should().HaveCountGreaterThanOrEqualTo(2, "the Guid key and the TokenHash unique constraint");
    }

    [Fact]
    public async Task EnsureAsync_CreatesTheLookupIndexes_OnTheirColumns()
    {
        await GrantSqlIndexes.EnsureAsync(_f.Grants, _f.Invitations, c => new SqLiteIndexManager(c));

        Columns(ResourceGrantMapping.Table, GrantSqlIndexes.GrantsByUser).Should().Equal("UserGuid", "ResourceType");
        Columns(ResourceGrantMapping.Table, GrantSqlIndexes.GrantsByResource).Should().Equal("ResourceType", "ResourceId");
        Columns(InvitationMapping.Table, GrantSqlIndexes.InvitationsByEmail).Should().Equal("Email");
    }

    /// <summary>The index's columns in order, as SQLite reports them — not parsed from its SQL text.</summary>
    private List<string> Columns(string table, string index)
    {
        _f.IndexSql(table).Should().Contain(sql => sql.Contains(index), $"index {index} must exist on {table}");
        using var connection = new SqliteConnection(_f.Settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info({index})";
        using var reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read()) columns.Add(reader.GetString(2));
        return columns;
    }

    [Fact]
    public async Task EnsureAsync_IsIdempotent()
    {
        await GrantSqlIndexes.EnsureAsync(_f.Grants, _f.Invitations, c => new SqLiteIndexManager(c));
        var before = _f.IndexSql(ResourceGrantMapping.Table).Count + _f.IndexSql(InvitationMapping.Table).Count;

        await GrantSqlIndexes.EnsureAsync(_f.Grants, _f.Invitations, c => new SqLiteIndexManager(c));

        (_f.IndexSql(ResourceGrantMapping.Table).Count + _f.IndexSql(InvitationMapping.Table).Count).Should().Be(before);
    }

    [Fact]
    public async Task EnsureAsync_WithoutSettings_Refuses()
    {
        var ensure = () => GrantSqlIndexes.EnsureAsync(new SqlResourceGrantStore<SqLiteConnector>(), new SqlInvitationStore<SqLiteConnector>(), c => new SqLiteIndexManager(c));

        await ensure.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task TheGrantService_RunsOnTheSqlStores()
    {
        await GrantSqlIndexes.EnsureAsync(_f.Grants, _f.Invitations, c => new SqLiteIndexManager(c));
        var table = new GrantOptions().DefineRole("owner", "read", "edit", "share").DefineRole("viewer", "read").ResourceType("doc").Build();
        var service = new Services.GrantService(_f.Grants, table, new Birko.Time.SystemDateTimeProvider());
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var doc = Guid.NewGuid();

        await service.GrantOwnerOnCreateAsync(owner, "doc", doc);
        await service.GrantAsync(owner, other, "doc", doc, "viewer");

        (await service.CanAsync(other, "doc", doc, "read")).Should().BeTrue();
        (await service.CanAsync(other, "doc", doc, "edit")).Should().BeFalse();
        (await service.RevokeAsync(owner, other, "doc", doc)).Should().BeTrue();
        (await service.CanAsync(other, "doc", doc, "read")).Should().BeFalse();
    }
}
