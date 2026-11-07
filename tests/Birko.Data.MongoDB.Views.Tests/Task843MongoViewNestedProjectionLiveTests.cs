using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.MongoDB.Views;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson.Serialization.Attributes;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-843: a view that projects a nested member (<c>r.Sub.X</c>) was recorded as source field <c>X</c> by the
/// shared builder, so MongoDB projected <c>"$X"</c>, the document's top-level field (measured on MongoDB 7,
/// 2026-11-07: every nested view property read its same-named top-level field). The projection now reads the whole
/// path, <c>"$Sub.X"</c>, each nested segment named as the driver stores it (<c>[BsonElement]</c>, a nested id).
/// Gated on <c>BIRKO_MONGO_HOST</c>.
/// </summary>
public class Task843MongoViewNestedProjectionLiveTests
{
    private readonly ITestOutputHelper _output;
    public Task843MongoViewNestedProjectionLiveTests(ITestOutputHelper output) => _output = output;

    private string? Host()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"))) throw new InvalidOperationException(message);
        return null;
    }

    public class T843Deeper { public int Level { get; set; } }

    public class T843Sub
    {
        public Guid Id { get; set; }
        public int X { get; set; }
        [BsonElement("ren")] public string? Renamed { get; set; }
        public T843Deeper? Deep { get; set; }
    }

    /// <summary>Top-level members named like the nested ones, with different values, so a flattened read shows.</summary>
    public class T843Row : AbstractModel
    {
        public int X { get; set; }
        public string? Renamed { get; set; }
        public int Level { get; set; }
        public T843Sub Sub { get; set; } = new();
    }

    public class FlatView
    {
        public int TopX { get; set; }
        public int SubX { get; set; }
        public Guid SubId { get; set; }
        public string? SubRenamed { get; set; }
        public int? Level { get; set; }
    }

    private static ViewDefinition Nested() => new ViewDefinitionBuilder<FlatView>()
        .From<T843Row>()
        .Select<T843Row, int>(r => r.X, v => v.TopX)
        .Select<T843Row, int>(r => r.Sub.X, v => v.SubX)
        .Select<T843Row, Guid>(r => r.Sub.Id, v => v.SubId)
        .Select<T843Row, string?>(r => r.Sub.Renamed, v => v.SubRenamed)
        .Select<T843Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
        .Build();

    private static IEnumerable<(int, int, Guid, string?, int?)> Tuples(IEnumerable<FlatView> rows)
        => rows.Select(v => (v.TopX, v.SubX, v.SubId, v.SubRenamed, v.Level));

    private async Task Run(Func<MongoDBClient, List<FlatView>, Task> body)
    {
        var host = Host();
        if (host == null) return;
        var settings = new Settings(host, "birko_task843_" + Guid.NewGuid().ToString("N"));
        var client = new MongoDBClient(settings);
        try
        {
            var store = new AsyncMongoDBStore<T843Row>();
            store.SetSettings(settings);
            var oracle = new List<FlatView>();
            for (var i = 0; i < 6; i++)
            {
                var row = new T843Row
                {
                    X = 10 + i, Renamed = "top" + i, Level = 100 + i,
                    Sub = new T843Sub { Id = Guid.NewGuid(), X = 5 - i, Renamed = "n" + i, Deep = i == 3 ? null : new T843Deeper { Level = i % 3 } },
                };
                await store.CreateAsync(row);
                oracle.Add(new FlatView { TopX = row.X, SubX = row.Sub.X, SubId = row.Sub.Id, SubRenamed = row.Sub.Renamed, Level = row.Sub.Deep?.Level });
            }
            await body(client, oracle);
        }
        finally
        {
            await client.Database.Client.DropDatabaseAsync(settings.Name);
        }
    }

    [Fact]
    public Task A_nested_projection_reads_the_nested_values() => Run(async (client, oracle) =>
    {
        var rows = (await new MongoViewStore<FlatView>(client, Nested()).QueryAsync()).ToList();
        Tuples(rows).Should().BeEquivalentTo(Tuples(oracle));
    });

    [Fact]
    public Task A_filter_a_count_and_an_order_on_a_nested_projection_use_the_nested_value() => Run(async (client, oracle) =>
    {
        var store = new MongoViewStore<FlatView>(client, Nested());
        (await store.QueryAsync(v => v.SubX >= 3)).Select(v => v.SubX).Should().BeEquivalentTo(new[] { 5, 4, 3 });
        (await store.CountAsync(v => v.Level == 1)).Should().Be(oracle.Count(v => v.Level == 1));
        (await store.QueryAsync(null, OrderBy<FlatView>.ByDescending(v => v.SubX))).Select(v => v.SubX)
            .Should().Equal(oracle.Select(v => v.SubX).OrderByDescending(x => x));
    });

    [Fact]
    public Task A_persistent_view_of_a_nested_projection_reads_the_nested_values() => Run(async (client, oracle) =>
    {
        var definition = new ViewDefinitionBuilder<FlatView>()
            .HasName("t843_nested").HasQueryMode(ViewQueryMode.Persistent)
            .From<T843Row>()
            .Select<T843Row, int>(r => r.X, v => v.TopX)
            .Select<T843Row, int>(r => r.Sub.X, v => v.SubX)
            .Select<T843Row, Guid>(r => r.Sub.Id, v => v.SubId)
            .Select<T843Row, string?>(r => r.Sub.Renamed, v => v.SubRenamed)
            .Select<T843Row, int?>(r => r.Sub.Deep!.Level, v => v.Level)
            .Build();
        await new MongoViewManager(client).EnsureAsync(definition);

        var rows = (await new MongoViewStore<FlatView>(client, definition).QueryAsync()).ToList();
        Tuples(rows).Should().BeEquivalentTo(Tuples(oracle));
    });
}
