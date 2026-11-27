using System;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// <see cref="SqLiteSqlStoreFactory"/> built from settings, as a host does without a connection string: it creates a
/// missing database directory, and its connector and stores reach that database. The older <c>SqLiteStoreFactory</c>
/// (TASK-033) that these tests covered was removed at TASK-1001; its relative-path base directory, sync
/// <c>GetStore</c> and <c>AddSqLiteStores</c> went with it.
/// </summary>
public class SqLiteSqlStoreFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-factory-{Guid.NewGuid():N}");

    public void Dispose() => TestTempDirectory.Delete(_root);

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets")
                .HasPrimary(x => x.Guid)
                .HasUnique(x => x.Guid);
            map.Property(x => x.Name);
        }
    }

    [Fact]
    public void Constructor_creates_missing_directory()
    {
        var location = Path.Combine(_root, "nested", "db");
        Directory.Exists(location).Should().BeFalse();

        var factory = new SqLiteSqlStoreFactory(new SqLiteSettings(location, "app.db"));

        Directory.Exists(location).Should().BeTrue();
        factory.SqLiteSettings.Path.Should().Be(Path.Combine(location, "app.db"));
    }

    [Fact]
    public void GetConnector_and_stores_are_wired_to_the_configured_database()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteSqlStoreFactory(new SqLiteSettings(_root, "app.db"));
        var connector = factory.GetConnector();
        connector.CreateTable(new[] { typeof(Widget) });

        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Widgets'";
        ((long)cmd.ExecuteScalar()!).Should().Be(1);

        factory.GetAsyncStore<Widget>().Should().BeOfType<AsyncSQLiteStore<Widget>>();
    }
}
