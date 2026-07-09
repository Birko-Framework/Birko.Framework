using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;
using Birko.Data.SQL.SQLite.IndexManagement;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// CR-H093: SqLiteIndexManager.ListAsync used `new` instead of `override`, so the inherited
/// GetInfoAsync and any base/interface-typed caller ran the base query that emits an empty
/// column-name literal. Now an override, these paths must return the real PRAGMA-derived columns.
/// </summary>
public class SqLiteIndexManagerDispatchTests : IDisposable
{
    private readonly string _root;

    public SqLiteIndexManagerDispatchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-idx-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private SqLiteConnector NewConnectorWithIndex()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "idx.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Widget) });

        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE INDEX ix_widget_name ON Widgets(Name)";
        cmd.ExecuteNonQuery();
        return connector;
    }

    [Fact]
    public async Task GetInfoAsync_ReturnsRealColumnName()
    {
        var manager = new SqLiteIndexManager(NewConnectorWithIndex());

        // GetInfoAsync is inherited from the base and calls the (now virtual) ListAsync — which must
        // dispatch to the SQLite PRAGMA override, not the base empty-column query.
        var info = await manager.GetInfoAsync("ix_widget_name", "Widgets");

        info.Should().NotBeNull();
        info!.Fields.Should().ContainSingle();
        info.Fields[0].Name.Should().Be("Name");
    }

    [Fact]
    public async Task BaseTypedReference_DispatchesToSqliteOverride()
    {
        SqlIndexManager manager = new SqLiteIndexManager(NewConnectorWithIndex());

        var list = await manager.ListAsync("Widgets");

        var index = list.Single(i => i.Name == "ix_widget_name");
        index.Fields.Should().ContainSingle();
        index.Fields[0].Name.Should().Be("Name", "the override supplies the real column name, not an empty literal");
    }
}
