using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-841: Cosmos DB has no collation, so a string <c>IN</c> and a string <c>ARRAY_CONTAINS</c> match only the exact
/// string, and chunks of distinct C# strings are disjoint. The first test is the premise (measured on the emulator,
/// vnext-preview, 2026-11-07): if it ever fails, string lists must not be split. Live, gated on
/// <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task841StringChunkingLiveTests : IDisposable
{
    private readonly CosmosLive _live;
    private readonly ITestOutputHelper _output;
    private readonly List<CosmosClient> _clients = new();

    public Task841StringChunkingLiveTests(ITestOutputHelper output)
    {
        _live = new CosmosLive(output);
        _output = output;
    }

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        _live.Dispose();
    }

    /// <summary>Pairs that a case-, whitespace- or accent-insensitive comparison would treat as one value.</summary>
    private static readonly string[] Probes =
    {
        "a", "A", "a ", "\u00e9", "e\u0301", "e", "Stra\u00dfe", "STRASSE", "strasse",
    };

    /// <summary>Chunks of 3, and a query-text limit of 2,000 characters that the whole list is over.</summary>
    private const int Chunk = 3;
    private const int Max = 2000;

    private sealed class ChunkedAsyncStore : AsyncCosmosDBStore<InListDoc>
    {
        public ChunkedAsyncStore(Container container) : base(container) { }
        protected override int InListChunkSize => Chunk;
        protected override int MaxQueryTextLength => Max;
    }

    private sealed class ChunkedStore : CosmosDBStore<InListDoc>
    {
        public ChunkedStore(Container container) : base(container) { }
        protected override int InListChunkSize => Chunk;
        protected override int MaxQueryTextLength => Max;
    }

    private async Task<(Container Container, List<InListDoc> Seed)?> SetUpAsync(IEnumerable<string> names)
    {
        if (!_live.Ready()) return null;
        var (conn, db, settings) = _live.Next();
        var client = new CosmosClient(conn, settings.GetCosmosClientOptions());
        _clients.Add(client);
        var database = (await client.CreateDatabaseIfNotExistsAsync(db)).Database;
        var container = (await database.CreateContainerIfNotExistsAsync(nameof(InListDoc), "/id")).Container;
        var docs = names.Select((n, i) => new InListDoc { Guid = Guid.NewGuid(), Name = n, N = i }).ToList();
        await new AsyncCosmosDBStore<InListDoc>(container).CreateAsync(docs);
        return (container, docs);
    }

    [Fact]
    public async Task A_string_IN_and_ARRAY_CONTAINS_match_only_the_exact_string()
    {
        var set = await SetUpAsync(Probes);
        if (set == null) return;
        var (container, _) = set.Value;
        var store = new AsyncCosmosDBStore<InListDoc>(container);

        foreach (var probe in Probes)
        {
            // Two values, so the provider renders IN rather than "=".
            var list = new List<string> { probe, "no-such-name" };
            Expression<Func<InListDoc, bool>> filter = x => list.Contains(x.Name!);

            var text = container.GetItemLinqQueryable<InListDoc>().Where(filter).ToQueryDefinition().QueryText;
            text.Should().Contain(" IN (", "the base store's LINQ renders a list Contains as IN");
            var viaIn = (await store.ReadAsync(filter)).Select(d => d.Name).ToList();

            var path = text.Substring(text.IndexOf("WHERE ", StringComparison.Ordinal) + 6);
            path = path.Substring(0, path.IndexOf(" IN (", StringComparison.Ordinal)).Trim('(', ' ');
            var query = new QueryDefinition($"SELECT VALUE {path} FROM root WHERE ARRAY_CONTAINS(@p, {path})")
                .WithParameter("@p", list.ToArray());
            var viaArray = new List<string>();
            using (var it = container.GetItemQueryIterator<string>(query))
                while (it.HasMoreResults) viaArray.AddRange(await it.ReadNextAsync());

            _output.WriteLine($"{Escape(probe)}: IN -> [{string.Join(", ", viaIn.Select(Escape))}], "
                            + $"ARRAY_CONTAINS -> [{string.Join(", ", viaArray.Select(Escape))}]");
            viaIn.Should().Equal(new[] { probe }, $"IN matches only {Escape(probe)}, ordinally");
            viaArray.Should().Equal(new[] { probe }, $"ARRAY_CONTAINS matches only {Escape(probe)}, ordinally");
        }
    }

    private static string Escape(string? s)
        => s == null ? "null" : "\"" + string.Concat(s.Select(c => c < 128 && c != ' ' ? c.ToString() : $"\\u{(int)c:x4}")) + "\"";

    /// <summary>A long name, so the whole list is over <see cref="Max"/> and one chunk is under it.</summary>
    internal static string Long(int i) => $"n{i}-" + new string('x', 300);

    private static readonly string[] Names = Probes.Concat(Enumerable.Range(0, 12).Select(Long)).ToArray();

    /// <summary>
    /// One of each confusable pair, long names, near misses of two stored ones (case, a trailing space), an absent one,
    /// and duplicates in later chunks: 12 distinct values, 9 of them stored.
    /// </summary>
    private static List<string> Wanted() => new()
    {
        "a", "e\u0301", "STRASSE", Long(0), Long(1), Long(2), Long(3), Long(4), Long(5).ToUpperInvariant(), Long(6) + " ",
        "absent", "a", Long(0), "e\u0301", Long(11),
    };

    [Fact]
    public async Task A_chunked_string_read_and_count_equal_the_oracle_async_and_sync()
    {
        var set = await SetUpAsync(Names);
        if (set == null) return;
        var (container, seed) = set.Value;
        var wanted = Wanted();
        Expression<Func<InListDoc, bool>> filter = x => wanted.Contains(x.Name!);
        Expression<Func<InListDoc, bool>> conjunct = x => x.N >= 0 && wanted.Contains(x.Name!);

        var oracle = seed.Where(d => wanted.Contains(d.Name!, StringComparer.Ordinal)).Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        oracle.Should().HaveCount(9);

        var whole = container.GetItemLinqQueryable<InListDoc>().Where(filter).ToQueryDefinition().QueryText.Length;
        _output.WriteLine($"one query would be {whole} characters, over the lowered limit of {Max}");
        whole.Should().BeGreaterThan(Max, "the list cannot run as one query, so only chunks can answer");

        var async = new ChunkedAsyncStore(container);
        var sync = new ChunkedStore(container);
        foreach (var f in new[] { filter, conjunct })
        {
            Sorted(await async.ReadAsync(f)).Should().Equal(oracle);
            Sorted(sync.Read(f, null, null, null)).Should().Equal(oracle);
            (await async.CountAsync(f)).Should().Be(oracle.Count);
            sync.Count(f).Should().Be(oracle.Count);
        }
    }

    private static List<string?> Sorted(IEnumerable<InListDoc> docs) => docs.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
}

/// <summary>TASK-841, offline: the Cosmos planner splits a string list and deduplicates it ordinally.</summary>
public class Task841StringPlannerTests
{
    [Fact]
    public void A_string_list_is_split_and_deduplicated_by_ordinal_equality()
    {
        var names = new List<string?> { "a", "A", "a ", "\u00e9", "e\u0301", "a", null, null, "A" };
        Expression<Func<InListDoc, bool>> filter = x => names.Contains(x.Name);
        var chunks = CosmosInListChunking.Plan(filter, "read", 2, 1_000_000, false, _ => 0);
        chunks.Should().HaveCount(3, "6 distinct values (null once) in chunks of 2");
        var values = chunks!.SelectMany(c => ((System.Collections.IEnumerable)((ConstantExpression)((MethodCallExpression)c.Body).Object!).Value!).Cast<string?>()).ToList();
        values.Should().Equal("a", "A", "a ", "\u00e9", "e\u0301", null);
    }
}
