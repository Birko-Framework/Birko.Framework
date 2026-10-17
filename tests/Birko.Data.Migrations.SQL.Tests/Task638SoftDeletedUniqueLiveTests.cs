using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.Migrations.SQL.Tests;

[Table("T638Slugs")]
public class T638Slugged : AbstractModel, ISluggable, ISoftDeletable
{
    public string? Name { get; set; }

    [MaxLengthField(100)]
    [IndexedField("UX_T638Slugs_Slug", IsUnique: true)]
    public string? Slug { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? GetSlugSource() => Name;
}

[Table("T638Defaults")]
public class T638Defaultable : AbstractModel, IDefaultMarker, ISoftDeletable
{
    public string? Name { get; set; }
    public bool IsDefault { get; set; }

    [IndexedField("UX_T638Defaults_Marker", IsUnique: true, WhereNotNull = new[] { nameof(DefaultMarker) })]
    public bool? DefaultMarker { get; set; }

    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// TASK-638, live: a soft-deleted row keeps its slug and default marker under the unique indexes the models declare,
/// while every probe of the Sluggable and Default decorators reads through SoftDelete and cannot see it. Composed in
/// <c>StoreWrapperBuilder</c>'s order: Default → Sluggable → SoftDelete → raw store.
/// </summary>
public abstract class Task638SoftDeletedUniqueLiveTestsBase<DB> : IDisposable where DB : AbstractConnector
{
    protected readonly ITestOutputHelper Output;
    protected Task638SoftDeletedUniqueLiveTestsBase(ITestOutputHelper output) => Output = output;

    protected abstract string? HostVariable { get; }
    protected abstract Birko.Configuration.Settings Settings();

    protected bool RequireServer()
    {
        if (HostVariable == null || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return true;
        var message = $"SKIPPED: set {HostVariable} to run these live tests; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        Output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    private async Task<AsyncDataBaseBulkStore<DB, T>> RawAsync<T>() where T : AbstractModel
    {
        var store = new AsyncDataBaseBulkStore<DB, T>();
        store.SetSettings(Settings());
        Drop(store.Connector, typeof(T));
        await store.InitAsync();
        return store;
    }

    private static IAsyncBulkStore<T> SoftDeleted<T>(IAsyncBulkStore<T> raw) where T : AbstractModel, ISoftDeletable
        => new AsyncSoftDeleteBulkStoreWrapper<IAsyncBulkStore<T>, T>(raw, new SystemDateTimeProvider());

    private static void Drop(AbstractConnector? connector, Type type)
    {
        try { connector?.DropTable(new[] { type }); } catch { }
    }

    public void Dispose()
    {
        if (HostVariable != null && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HostVariable))) return;
        var probe = new AsyncDataBaseBulkStore<DB, T638Slugged>();
        probe.SetSettings(Settings());
        Drop(probe.Connector, typeof(T638Slugged));
        Drop(probe.Connector, typeof(T638Defaultable));
    }

    [Fact]
    public async Task A_slug_whose_row_was_soft_deleted_does_not_refuse_the_next_create()
    {
        if (!RequireServer()) return;
        var raw = await RawAsync<T638Slugged>();
        var slugs = new AsyncSluggableBulkStoreWrapper<IAsyncBulkStore<T638Slugged>, T638Slugged>(SoftDeleted<T638Slugged>(raw));

        var old = new T638Slugged { Name = "Widget" };
        await slugs.CreateAsync(old);
        await slugs.DeleteAsync(old);

        var fresh = new T638Slugged { Name = "Widget" };
        var create = async () => await slugs.CreateAsync(fresh);
        await create.Should().NotThrowAsync();

        var stored = (await raw.ReadAsync(null, null, null, null)).ToList();
        Output.WriteLine(string.Join(", ", stored.Select(s => $"{s.Slug} deleted={s.DeletedAt != null}")));
        stored.Should().HaveCount(2);
        stored.Single(s => s.Guid == old.Guid).Slug.Should().Be("widget", "a soft-deleted row keeps its slug");
        fresh.Slug.Should().Be("widget-2");
        stored.Single(s => s.Guid == fresh.Guid).Slug.Should().Be("widget-2");
        (await slugs.ReadAsync(x => x.Name == "Widget", null, null, null)).Should().ContainSingle(s => s.Guid == fresh.Guid);
    }

    [Fact]
    public async Task A_soft_deleted_former_default_does_not_refuse_the_next_promotion()
    {
        if (!RequireServer()) return;
        var raw = await RawAsync<T638Defaultable>();
        var defaults = new AsyncDefaultStoreWrapper<IAsyncBulkStore<T638Defaultable>, T638Defaultable>(SoftDeleted<T638Defaultable>(raw));

        var old = new T638Defaultable { Name = "old", IsDefault = true };
        await defaults.CreateAsync(old);
        await defaults.DeleteAsync(old);

        var fresh = new T638Defaultable { Name = "new", IsDefault = true };
        var create = async () => await defaults.CreateAsync(fresh);
        await create.Should().NotThrowAsync();

        var stored = (await raw.ReadAsync(null, null, null, null)).ToList();
        Output.WriteLine(string.Join(", ", stored.Select(s => $"{s.Name} default={s.IsDefault} marker={s.DefaultMarker} deleted={s.DeletedAt != null}")));
        stored.Where(s => s.DeletedAt == null).Should().ContainSingle(s => s.IsDefault && s.Guid == fresh.Guid);
        var retired = stored.Single(s => s.Guid == old.Guid);
        retired.IsDefault.Should().BeFalse();
        retired.DefaultMarker.Should().BeNull();
    }

    [Fact]
    public async Task A_default_soft_deleted_by_filter_does_not_refuse_the_next_promotion()
    {
        if (!RequireServer()) return;
        var raw = await RawAsync<T638Defaultable>();
        var defaults = new AsyncDefaultStoreWrapper<IAsyncBulkStore<T638Defaultable>, T638Defaultable>(SoftDeleted<T638Defaultable>(raw));

        await defaults.CreateAsync(new T638Defaultable { Name = "old", IsDefault = true });
        await defaults.DeleteAsync(x => x.Name == "old");
        await defaults.CreateAsync(new T638Defaultable { Name = "new", IsDefault = true });

        var stored = (await raw.ReadAsync(null, null, null, null)).ToList();
        stored.Single(s => s.Name == "old").Should().Match<T638Defaultable>(s => s.DeletedAt != null && !s.IsDefault && s.DefaultMarker == null);
        stored.Single(s => s.Name == "new").Should().Match<T638Defaultable>(s => s.IsDefault && s.DefaultMarker == true);
    }
}

public sealed class SqliteTask638SoftDeletedUniqueLiveTests : Task638SoftDeletedUniqueLiveTestsBase<SqLiteConnector>
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"birko-t638-{Guid.NewGuid():N}.db");
    public SqliteTask638SoftDeletedUniqueLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => null;
    protected override Birko.Configuration.Settings Settings() => new SqLiteSettings(Path.GetDirectoryName(_path)!, Path.GetFileName(_path));
}

public sealed class PostgreSqlTask638SoftDeletedUniqueLiveTests : Task638SoftDeletedUniqueLiveTestsBase<PostgreSQLConnector>
{
    public PostgreSqlTask638SoftDeletedUniqueLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_PG_HOST";
    protected override Birko.Configuration.Settings Settings() => new PostgreSqlSettings(Environment.GetEnvironmentVariable("BIRKO_PG_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_PG_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_PG_USER") ?? "postgres",
        Environment.GetEnvironmentVariable("BIRKO_PG_PASSWORD") ?? "postgres")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_PG_PORT"), out var p) ? p : 5432 };
}

public sealed class MySqlTask638SoftDeletedUniqueLiveTests : Task638SoftDeletedUniqueLiveTestsBase<MySQLConnector>
{
    public MySqlTask638SoftDeletedUniqueLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MYSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MySqlSettings(Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root",
        Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root")
    { Port = int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306 };
}

public sealed class MSSqlTask638SoftDeletedUniqueLiveTests : Task638SoftDeletedUniqueLiveTestsBase<MSSqlConnector>
{
    public MSSqlTask638SoftDeletedUniqueLiveTests(ITestOutputHelper output) : base(output) { }
    protected override string? HostVariable => "BIRKO_MSSQL_HOST";
    protected override Birko.Configuration.Settings Settings() => new MSSqlSettings(Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST")!,
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa",
        Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd",
        int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433)
    { TrustServerCertificate = true };
}
