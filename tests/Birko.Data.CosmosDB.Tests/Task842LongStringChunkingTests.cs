using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-842, live: chunks are cut by serialized length, not only by count. 1,200 distinct 600-character strings put one
/// 1,000-value chunk past Cosmos DB's 524,288-character query limit; before TASK-842 the default store refused that
/// read by name. Run with the store's DEFAULT chunk size and limit. Gated on <c>BIRKO_COSMOS_CONNECTION</c>.
/// </summary>
public sealed class Task842LongStringChunkingLiveTests : IDisposable
{
    private readonly CosmosLive _live;
    private readonly ITestOutputHelper _output;
    private readonly List<CosmosClient> _clients = new();

    public Task842LongStringChunkingLiveTests(ITestOutputHelper output)
    {
        _live = new CosmosLive(output);
        _output = output;
    }

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        _live.Dispose();
    }

    private sealed class CountingHandler : RequestHandler
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public override Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _count);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private async Task<(Container Container, CountingHandler Requests)?> SetUpAsync(IEnumerable<InListDoc> seed)
    {
        if (!_live.Ready()) return null;
        var (conn, db, settings) = _live.Next();
        var handler = new CountingHandler();
        var options = settings.GetCosmosClientOptions();
        options.CustomHandlers.Add(handler);
        var client = new CosmosClient(conn, options);
        _clients.Add(client);
        var database = (await client.CreateDatabaseIfNotExistsAsync(db)).Database;
        var container = (await database.CreateContainerIfNotExistsAsync(nameof(InListDoc), "/id")).Container;
        await new AsyncCosmosDBStore<InListDoc>(container).CreateAsync(seed.ToList());
        return (container, handler);
    }

    /// <summary>A distinct 600-character string.</summary>
    internal static string Long(int i) => $"k{i:D5}-" + new string('y', 593);

    /// <summary>1,200 distinct long strings, with duplicates of the first ones at the end.</summary>
    private static List<string> Wanted()
        => Enumerable.Range(0, 1200).Select(Long).Concat(Enumerable.Range(0, 20).Select(Long)).ToList();

    /// <summary>
    /// Forty wanted names spread over the whole list (past the first 1,000 included), and near misses that are not
    /// in it: a case variant and a trailing space.
    /// </summary>
    private static List<InListDoc> Seed()
    {
        var docs = Enumerable.Range(0, 40).Select(i => new InListDoc { Guid = Guid.NewGuid(), Name = Long(i * 30), N = i }).ToList();
        docs.Add(new InListDoc { Guid = Guid.NewGuid(), Name = Long(1).ToUpperInvariant(), N = 100 });
        docs.Add(new InListDoc { Guid = Guid.NewGuid(), Name = Long(2) + " ", N = 101 });
        docs.Add(new InListDoc { Guid = Guid.NewGuid(), Name = "short", N = 102 });
        return docs;
    }

    private static List<string?> Sorted(IEnumerable<InListDoc> docs) => docs.Select(d => d.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    [Fact]
    public async Task A_long_string_list_one_count_chunk_overflows_is_read_and_counted_equal_to_the_oracle()
    {
        var seed = Seed();
        var set = await SetUpAsync(seed);
        if (set == null) return;
        var (container, _) = set.Value;
        var wanted = Wanted();
        Expression<Func<InListDoc, bool>> filter = x => wanted.Contains(x.Name!);
        Expression<Func<InListDoc, bool>> conjunct = x => x.N >= 0 && wanted.Contains(x.Name!);

        // The premise: one 1,000-value chunk of these strings is over the limit as one query.
        var thousand = wanted.Take(CosmosInListChunking.DefaultChunkSize).ToList();
        var chunkText = container.GetItemLinqQueryable<InListDoc>().Where(x => thousand.Contains(x.Name!)).ToQueryDefinition().QueryText.Length;
        _output.WriteLine($"a 1,000-value chunk would be {chunkText} characters; the limit is {CosmosInListChunking.DefaultMaxQueryTextLength}");
        chunkText.Should().BeGreaterThan(CosmosInListChunking.DefaultMaxQueryTextLength);

        var oracle = Sorted(seed.Where(d => wanted.Contains(d.Name!, StringComparer.Ordinal)));
        oracle.Should().HaveCount(40);

        var async = new AsyncCosmosDBStore<InListDoc>(container);
        var sync = new CosmosDBStore<InListDoc>(container);
        foreach (var f in new[] { filter, conjunct })
        {
            Sorted(await async.ReadAsync(f)).Should().Equal(oracle);
            Sorted(sync.Read(f, null, null, null)).Should().Equal(oracle);
            (await async.CountAsync(f)).Should().Be(oracle.Count);
            sync.Count(f).Should().Be(oracle.Count);
        }
    }

    [Fact]
    public async Task A_single_value_too_large_for_any_chunk_is_refused_by_name_before_any_request()
    {
        var set = await SetUpAsync(new[] { new InListDoc { Guid = Guid.NewGuid(), Name = "short", N = 1 } });
        if (set == null) return;
        var (container, requests) = set.Value;
        var huge = "h" + new string('z', 600_000);
        var names = new List<string> { "short", huge };
        Expression<Func<InListDoc, bool>> filter = x => names.Contains(x.Name!);

        var async = new AsyncCosmosDBStore<InListDoc>(container);
        var sync = new CosmosDBStore<InListDoc>(container);
        await async.CountAsync(); // initialise outside the counted window
        sync.Count();

        var before = requests.Count;
        var ex = (await async.Invoking(s => s.ReadAsync(filter)).Should().ThrowAsync<NotSupportedException>()).Which;
        _output.WriteLine(ex.Message);
        ex.Message.Should().Contain("Cosmos DB read of InListDoc").And.Contain("524288").And.Contain("one value");
        ex.Message.Should().NotContain("zzzzzzzzzz", "a refusal never names the values");
        await async.Invoking(s => s.CountAsync(filter)).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB count of*one value*");
        sync.Invoking(s => s.Read(filter, null, null, null).ToList()).Should().Throw<NotSupportedException>().WithMessage("*one value*");
        sync.Invoking(s => s.Count(filter)).Should().Throw<NotSupportedException>().WithMessage("*one value*");
        await async.Invoking(s => s.DeleteAsync(filter)).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB delete of*one value*");
        requests.Count.Should().Be(before, "a refusal is decided before any request");
        (await async.CountAsync()).Should().Be(1, "nothing was deleted");
    }
}

/// <summary>
/// TASK-842, offline: chunks are cut by measured length with the count as a ceiling. The length function stands in for
/// the store's rendering: a fixed overhead plus each listed value's length and a quoted separator.
/// </summary>
public class Task842LengthPlannerTests
{
    private const int Overhead = 50;

    private static int Values(Expression e, Func<object?, int> each)
    {
        var total = 0;
        new Collector(c => { foreach (var v in c) total += each(v); }).Visit(e);
        return total;
    }

    private sealed class Collector : ExpressionVisitor
    {
        private readonly Action<System.Collections.IEnumerable> _add;
        public Collector(Action<System.Collections.IEnumerable> add) => _add = add;
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is System.Collections.IList c) _add(c);
            return base.VisitConstant(node);
        }
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ConstantExpression { Value: { } closure } && node.Member is System.Reflection.FieldInfo f
                && f.GetValue(closure) is System.Collections.IList c) _add(c);
            return base.VisitMember(node);
        }
    }

    private static int Length(Expression<Func<InListDoc, bool>> f) => Overhead + Values(f, v => ((string?)v)?.Length + 4 ?? 6);

    private static List<string> Strings(int n, int length) => Enumerable.Range(0, n).Select(i => $"{i:D4}" + new string('s', length - 4)).ToList();

    private static List<string?> Flatten(IEnumerable<Expression<Func<InListDoc, bool>>> chunks)
    {
        var all = new List<string?>();
        foreach (var c in chunks) Values(c, v => { all.Add((string?)v); return 0; });
        return all;
    }

    [Fact]
    public void Long_strings_are_cut_by_length_under_the_budget_and_keep_every_value_once_in_order()
    {
        var names = Strings(50, 100);
        Expression<Func<InListDoc, bool>> filter = x => x.N > 0 && names.Contains(x.Name!);
        const int max = 1600;
        var budget = max - CosmosInListChunking.MarginFor(max);

        Length(filter).Should().BeGreaterThan(max, "the whole list is over the limit");
        var chunks = CosmosInListChunking.Plan(filter, "read", 1000, max, false, Length)!;
        chunks.Should().HaveCountGreaterThan(1, "50 values are under the count ceiling of 1000, so only length splits them");
        chunks.Select(Length).Should().OnlyContain(l => l <= budget);
        Flatten(chunks).Should().Equal(names);
    }

    [Fact]
    public void The_count_is_still_a_ceiling()
    {
        var names = Strings(23, 6);
        var chunks = CosmosInListChunking.Plan<InListDoc>(x => names.Contains(x.Name!), "read", 5, 1_000_000, false, Length)!;
        chunks.Should().HaveCount(5);
        Flatten(chunks).Should().Equal(names);
    }

    [Fact]
    public void A_list_under_the_count_whose_query_fits_runs_as_one_query()
    {
        var names = Strings(10, 100);
        CosmosInListChunking.Plan<InListDoc>(x => names.Contains(x.Name!), "read", 1000, 1_000_000, false, Length).Should().BeNull();
    }

    [Fact]
    public void A_chunk_the_estimate_lets_through_is_halved_until_its_measured_length_fits()
    {
        var names = Strings(40, 100);
        // The store renders three times what the JSON estimate expects (heavy escaping, say).
        int Tripled(Expression<Func<InListDoc, bool>> f) => Overhead + Values(f, v => 3 * (((string?)v)?.Length + 4 ?? 6));
        const int max = 4000;
        var chunks = CosmosInListChunking.Plan<InListDoc>(x => names.Contains(x.Name!), "count", 1000, max, false, Tripled)!;
        chunks.Select(Tripled).Should().OnlyContain(l => l <= max - CosmosInListChunking.MarginFor(max));
        Flatten(chunks).Should().Equal(names);
    }

    [Fact]
    public void A_single_value_no_chunk_can_hold_is_refused_naming_the_operation_lengths_and_limit_but_not_the_value()
    {
        var huge = "q" + new string('Q', 5000);
        var names = Strings(30, 20).Append(huge).ToList();
        var act = () => CosmosInListChunking.Plan<InListDoc>(x => names.Contains(x.Name!), "delete", 1000, 2000, false, Length);
        var ex = act.Should().Throw<NotSupportedException>().Which;
        ex.Message.Should().StartWith("Cosmos DB delete of InListDoc refused: one value")
            .And.Contain("31 distinct values").And.Contain("2000").And.Contain(Length(x => new List<string> { huge }.Contains(x.Name!)).ToString());
        ex.Message.Should().NotContain("QQQQ");
    }

    [Fact]
    public void A_list_of_one_value_too_long_for_the_limit_is_refused_too()
    {
        var names = new List<string> { new string('Q', 5000) };
        var act = () => CosmosInListChunking.Plan<InListDoc>(x => names.Contains(x.Name!), "read", 1000, 2000, false, Length);
        act.Should().Throw<NotSupportedException>().WithMessage("*one value*");
    }
}
