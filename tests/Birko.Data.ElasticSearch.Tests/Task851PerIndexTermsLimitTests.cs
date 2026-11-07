using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Elasticsearch.Net;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;
using ES = Birko.Data.ElasticSearch.ElasticSearch;
using IndexSettings = Birko.Data.ElasticSearch.Stores.IndexSettings;
using Settings = Birko.Data.ElasticSearch.Stores.Settings;

namespace Birko.Data.ElasticSearch.Tests;

public sealed class Task851Doc : AbstractModel
{
    public string? Name { get; set; }
}

/// <summary>
/// TASK-851: the terms limit TASK-835 splits a collection <c>Contains</c> at was one static for the whole process, so an
/// index with a smaller <c>index.max_terms_count</c> lowered it for every index. Each index now declares its own in
/// <see cref="IndexSettings.MaxTermsCount"/>; the store passes it to the translator per call, and the static is only the
/// default. Nothing here writes the static (rule 61).
/// </summary>
public sealed class Task851PerIndexTermsLimitTests : IDisposable
{
    private readonly ElasticLive _live;

    public Task851PerIndexTermsLimitTests(ITestOutputHelper output) => _live = new ElasticLive(output);

    public void Dispose() => _live.Dispose();

    private static List<Guid> Ids(int count) => Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();

    private static Settings Declaring(string location, string name, int? maxTermsCount) => new()
    {
        Location = location,
        Name = name,
        IndexSettings = maxTermsCount == null
            ? Array.Empty<IndexSettings>()
            : new[] { new IndexSettings { TypeName = typeof(Task851Doc).FullName!, MaxTermsCount = maxTermsCount } },
    };

    /// <summary>
    /// A client that answers every request in memory and records each <c>_count</c> body. Registered under the store's
    /// own settings id, which is unique per test, so no other test's client is touched.
    /// </summary>
    private static ConcurrentQueue<byte[]> Recording(Settings settings)
    {
        var bodies = new ConcurrentQueue<byte[]>();
        var connection = new ConnectionSettings(
                new SingleNodeConnectionPool(new Uri(settings.Location)),
                new InMemoryConnection(Encoding.UTF8.GetBytes("{\"count\":0}")))
            .DisableDirectStreaming()
            .OnRequestCompleted(call =>
            {
                if (call.Uri.AbsolutePath.EndsWith("/_count", StringComparison.Ordinal) && call.RequestBodyInBytes != null)
                    bodies.Enqueue(call.RequestBodyInBytes);
            });
        var clients = (ConcurrentDictionary<string, ElasticClient>)typeof(ES)
            .GetField("_clients", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        clients[settings.GetId()] = new ElasticClient(connection);
        return bodies;
    }

    /// <summary>The term count of every terms query in a recorded body, in order.</summary>
    private static List<int> TermsSizes(byte[] body)
    {
        var sizes = new List<int>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "terms" && p.Value.ValueKind == JsonValueKind.Object)
                        sizes.AddRange(p.Value.EnumerateObject().Where(f => f.Value.ValueKind == JsonValueKind.Array).Select(f => f.Value.GetArrayLength()));
                    else
                        Walk(p.Value);
                }
            }
            else if (e.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in e.EnumerateArray()) Walk(item);
            }
        }
        using var doc = JsonDocument.Parse(body);
        Walk(doc.RootElement);
        return sizes;
    }

    [Fact]
    public async Task Two_stores_with_different_limits_split_at_their_own_limit_in_parallel()
    {
        var name = "birko851" + Guid.NewGuid().ToString("N");
        var syncSettings = Declaring("http://task851.invalid:9200", name + "a", 3);
        var asyncSettings = Declaring("http://task851.invalid:9200", name + "b", 7);
        var syncBodies = Recording(syncSettings);
        var asyncBodies = Recording(asyncSettings);
        var sync = new ElasticSearchStore<Task851Doc>();
        sync.SetSettings(syncSettings);
        var async = new AsyncElasticSearchStore<Task851Doc>();
        async.SetSettings(asyncSettings);
        var ids = Ids(20);

        const int rounds = 50;
        await Task.WhenAll(Enumerable.Range(0, rounds).SelectMany(_ => new[]
        {
            Task.Run(() => sync.Count(x => ids.Contains(x.Guid!.Value))),
            Task.Run(() => async.CountAsync(x => ids.Contains(x.Guid!.Value))),
        }));

        syncBodies.Should().HaveCount(rounds);
        syncBodies.Should().OnlyContain(b => TermsSizes(b).SequenceEqual(new[] { 3, 3, 3, 3, 3, 3, 2 }));
        asyncBodies.Should().HaveCount(rounds);
        asyncBodies.Should().OnlyContain(b => TermsSizes(b).SequenceEqual(new[] { 7, 7, 6 }));
    }

    [Fact]
    public async Task A_store_without_a_declaration_splits_at_the_default_65536()
    {
        ES.MaxTermsCount.Should().Be(65_536);
        var settings = Declaring("http://task851.invalid:9200", "birko851" + Guid.NewGuid().ToString("N"), null);
        var bodies = Recording(settings);
        var store = new AsyncElasticSearchStore<Task851Doc>();
        store.SetSettings(settings);
        var ids = Ids(65_537);

        await store.CountAsync(x => ids.Contains(x.Guid!.Value));

        TermsSizes(bodies.Should().ContainSingle().Subject).Should().Equal(65_536, 1);
    }

    [Fact]
    public void The_translator_splits_at_the_limit_it_is_given_and_otherwise_at_the_default()
    {
        var ids = Ids(10);

        var given = ES.ParseRequiredFilterQuery<Task851Doc>(x => ids.Contains(x.Guid!.Value), maxTermsCount: 4);
        var defaulted = ES.ParseRequiredFilterQuery<Task851Doc>(x => ids.Contains(x.Guid!.Value));

        ((IQueryContainer)new QueryContainer(given)).Bool.Should.Should().HaveCount(3);
        defaulted.Should().BeOfType<TermsQuery>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_below_one_is_refused_not_wrapped_as_untranslatable(int limit)
    {
        var ids = Ids(2);

        var filter = () => ES.ParseFilterQuery<Task851Doc>(x => ids.Contains(x.Guid!.Value), limit);
        var expression = () => ES.ParseExpression((System.Linq.Expressions.Expression<Func<Task851Doc, bool>>)(x => ids.Contains(x.Guid!.Value)), maxTermsCount: limit);

        filter.Should().Throw<ArgumentOutOfRangeException>();
        expression.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Live: two indexes created with different <c>index.max_terms_count</c>, read in parallel by stores declaring them.
    /// A store on the small index that does not declare it is refused by the server, which is what the declaration is for.
    /// </summary>
    [Fact]
    public async Task Live_two_indexes_with_different_max_terms_count_each_read_at_their_own_limit()
    {
        if (!_live.Ready()) return;
        var small = _live.NewSettings();
        var large = _live.NewSettings();
        small.IndexSettings = new[] { new IndexSettings { TypeName = typeof(Task851Doc).FullName!, MaxTermsCount = 5 } };
        large.IndexSettings = new[] { new IndexSettings { TypeName = typeof(Task851Doc).FullName!, MaxTermsCount = 8 } };

        async Task<(AsyncElasticSearchStore<Task851Doc> Store, List<Guid> Stored)> Seed(Settings settings, int maxTermsCount)
        {
            var store = new AsyncElasticSearchStore<Task851Doc>();
            store.SetSettings(settings);
            await store.InitAsync(cid => cid
                .Settings(s => s.Setting("index.max_terms_count", maxTermsCount))
                .Map<Task851Doc>(m => m.AutoMap()));
            var stored = new List<Guid>();
            for (var i = 0; i < 6; i++)
                stored.Add(await store.CreateAsync(new Task851Doc { Name = "d" + i }));
            return (store, stored);
        }

        var (smallStore, smallStored) = await Seed(small, 5);
        var (largeStore, largeStored) = await Seed(large, 8);
        ElasticLive.Refresh();

        var smallIds = Ids(17).Concat(smallStored.Take(3)).ToList();
        var largeIds = Ids(17).Concat(largeStored.Take(4)).ToList();

        var reads = Enumerable.Range(0, 10).SelectMany(_ => new[]
        {
            Task.Run(async () => (await smallStore.ReadAsync(x => smallIds.Contains(x.Guid!.Value))).Select(d => d.Guid!.Value).ToList()),
            Task.Run(async () => (await largeStore.ReadAsync(x => largeIds.Contains(x.Guid!.Value))).Select(d => d.Guid!.Value).ToList()),
        }).ToList();
        var results = await Task.WhenAll(reads);

        for (var i = 0; i < results.Length; i += 2)
        {
            results[i].Should().BeEquivalentTo(smallStored.Take(3));
            results[i + 1].Should().BeEquivalentTo(largeStored.Take(4));
        }

        var undeclared = new AsyncElasticSearchStore<Task851Doc>();
        undeclared.SetSettings(new Settings { Location = small.Location, Name = small.Name });
        var refused = () => undeclared.CountAsync(x => smallIds.Contains(x.Guid!.Value));
        await refused.Should().ThrowAsync<Exception>();
    }
}
