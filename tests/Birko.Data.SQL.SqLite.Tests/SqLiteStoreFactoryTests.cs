using System;
using System.Data.Common;
using System.IO;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.SqLite;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// Tests for the backported <see cref="SqLiteStoreFactory"/> + <c>AddSqLiteStores</c> DI extension
/// (TASK-033): path resolution, eager directory creation, connector wiring, and DI registration.
/// </summary>
public class SqLiteStoreFactoryTests : IDisposable
{
    private readonly string _root;

    public SqLiteStoreFactoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-factory-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

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
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    [Fact]
    public void Constructor_creates_missing_directory()
    {
        var location = Path.Combine(_root, "nested", "db");
        Directory.Exists(location).Should().BeFalse();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = location, Name = "app.db" });

        Directory.Exists(location).Should().BeTrue();
        factory.Settings.Path.Should().Be(Path.Combine(location, "app.db"));
    }

    [Fact]
    public void Relative_location_is_resolved_against_base_directory()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions
        {
            Location = "data",
            Name = "app.db",
            BaseDirectory = _root,
        });

        factory.Settings.Path.Should().Be(Path.Combine(_root, "data", "app.db"));
        Directory.Exists(Path.Combine(_root, "data")).Should().BeTrue();
    }

    [Fact]
    public void Rooted_location_ignores_base_directory()
    {
        var rooted = Path.Combine(_root, "abs");
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions
        {
            Location = rooted,
            Name = "app.db",
            BaseDirectory = Path.Combine(_root, "should-be-ignored"),
        });

        factory.Settings.Path.Should().Be(Path.Combine(rooted, "app.db"));
    }

    [Fact]
    public void GetConnector_is_wired_to_the_configured_database()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "app.db" });
        var connector = factory.GetConnector();
        connector.CreateTable(new[] { typeof(Widget) });

        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Widgets'";
        ((long)cmd.ExecuteScalar()!).Should().Be(1);

        // GetStore hands back a store bound to the same shared settings instance.
        factory.GetStore<Widget>().Should().NotBeNull();
    }

    [Fact]
    public void AddSqLiteStores_registers_a_resolvable_singleton_factory()
    {
        var services = new ServiceCollection();
        services.AddSqLiteStores(o =>
        {
            o.Location = _root;
            o.Name = "app.db";
        });

        using var provider = services.BuildServiceProvider();
        var a = provider.GetService<ISqLiteStoreFactory>();
        var b = provider.GetService<ISqLiteStoreFactory>();

        a.Should().NotBeNull();
        a.Should().BeSameAs(b); // singleton
        a!.Settings.Path.Should().Be(Path.Combine(_root, "app.db"));
    }
}
