using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Tests;

public class InListDoc : AbstractModel
{
    public string? Name { get; set; }
    public int N { get; set; }
}

/// <summary>
/// TASK-834, offline: the planner that splits a long <c>ids.Contains(x.Col)</c> by TASK-833's rules. No request is
/// made here: the query-text length is supplied by the test.
/// </summary>
public class Task834InListPlannerTests
{
    private static List<Guid?> Ids(int n) => Enumerable.Range(0, n).Select(_ => (Guid?)Guid.NewGuid()).ToList();

    // 40 characters per value, as measured for Guids; the real store renders the query to get this.
    private static int FakeLength(Expression<Func<InListDoc, bool>> f) => CountValues(f) * 40;

    private static int CountValues(Expression e)
    {
        var total = 0;
        new Counter(v => total += v).Visit(e);
        return total;
    }

    private sealed class Counter : ExpressionVisitor
    {
        private readonly Action<int> _add;
        public Counter(Action<int> add) => _add = add;
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is System.Collections.ICollection c) _add(c.Count);
            return base.VisitConstant(node);
        }
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression is ConstantExpression { Value: { } closure } && node.Member is System.Reflection.FieldInfo f
                && f.GetValue(closure) is System.Collections.ICollection c) _add(c.Count);
            return base.VisitMember(node);
        }
    }

    private static IReadOnlyList<Expression<Func<InListDoc, bool>>>? Plan(Expression<Func<InListDoc, bool>> f, int chunk = 10, int max = 4000, bool sorted = false)
        => CosmosInListChunking.Plan(f, "read", chunk, max, sorted, FakeLength);

    [Fact]
    public void A_list_within_the_chunk_size_runs_as_one_query()
    {
        var ids = Ids(10);
        Plan(x => ids.Contains(x.Guid)).Should().BeNull();
    }

    [Fact]
    public void An_empty_list_is_never_chunked()
    {
        var ids = new List<Guid?>();
        Plan(x => ids.Contains(x.Guid), chunk: 1).Should().BeNull();
    }

    [Fact]
    public void A_list_under_AND_and_nested_AND_is_split_into_non_empty_disjoint_chunks()
    {
        var ids = Ids(25);
        var chunks = Plan(x => x.N > 0 && (x.Name != null && ids.Contains(x.Guid)))!;
        chunks.Should().HaveCount(3);
        var docs = ids.Select(g => new InListDoc { Guid = g, N = 1, Name = "a" }).ToList();
        var matched = chunks.Select(c => docs.Where(c.Compile()).Select(d => d.Guid).ToList()).ToList();
        matched.Should().OnlyContain(m => m.Count > 0);
        matched.SelectMany(m => m).Should().BeEquivalentTo(ids).And.OnlyHaveUniqueItems();
        matched.Select(m => m.Count).Should().Equal(10, 10, 5);
    }

    [Fact]
    public void Duplicates_are_dropped_so_chunks_stay_disjoint()
    {
        var distinct = Ids(15);
        var ids = distinct.Concat(distinct).Concat(new Guid?[] { null, null }).ToList();
        var chunks = Plan(x => ids.Contains(x.Guid))!;
        chunks.Should().HaveCount(2, "16 distinct values (15 Guids and one null) in chunks of 10");
    }

    [Fact]
    public void The_callers_expression_and_list_are_not_modified()
    {
        var ids = Ids(25);
        var copy = ids.ToList();
        Expression<Func<InListDoc, bool>> filter = x => x.N > 0 && ids.Contains(x.Guid);
        var before = filter.ToString();
        var body = filter.Body;
        Plan(filter).Should().HaveCount(3);
        filter.ToString().Should().Be(before);
        filter.Body.Should().BeSameAs(body);
        ids.Should().Equal(copy);
    }

    [Fact]
    public void The_largest_list_is_split()
    {
        var small = Ids(12);
        var large = Ids(30);
        var chunks = Plan(x => small.Contains(x.Guid) && large.Contains(x.Guid), max: 100_000)!;
        chunks.Should().HaveCount(3);
    }

    [Fact]
    public void An_array_list_rewritten_from_span_contains_is_split()
    {
        var ids = Ids(25).Select(g => g!.Value).ToArray();
        var filter = Birko.Data.Expressions.SpanContains.Rewrite<InListDoc>(x => ids.Contains(x.Guid!.Value))!;
        Plan(filter).Should().HaveCount(3);
    }

    public static IEnumerable<object[]> Unsplittable()
    {
        yield return new object[] { "OR", "under an OR" };
        yield return new object[] { "NOT IN", "NOT IN" };
        yield return new object[] { "negated group", "under a NOT" };
        yield return new object[] { "nested", "nested expression" };
    }

    private static Expression<Func<InListDoc, bool>> Shape(string shape, List<Guid?> ids) => shape switch
    {
        "OR" => x => ids.Contains(x.Guid) || x.N == 1,
        "NOT IN" => x => !ids.Contains(x.Guid),
        "negated group" => x => !(ids.Contains(x.Guid) && x.N == 1),
        "nested" => x => (x.N > 0 ? ids.Contains(x.Guid) : false),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    [Theory]
    [MemberData(nameof(Unsplittable))]
    public void An_unsplittable_list_past_the_ceiling_is_refused_naming_the_reason_and_not_the_values(string shape, string reason)
    {
        var ids = Ids(200);
        var act = () => Plan(Shape(shape, ids));
        var ex = act.Should().Throw<NotSupportedException>().Which;
        ex.Message.Should().Contain(reason).And.Contain("200 distinct values").And.Contain("4000").And.Contain("read");
        ex.Message.Should().NotContain(ids[0].ToString()!);
    }

    [Theory]
    [MemberData(nameof(Unsplittable))]
    public void An_unsplittable_list_within_the_ceiling_still_runs_as_one_query(string shape, string _)
    {
        var ids = Ids(50);
        Plan(Shape(shape, ids)).Should().BeNull();
    }

    [Fact]
    public void A_sorted_or_paged_read_past_the_ceiling_is_refused_and_within_it_runs_as_one_query()
    {
        var big = Ids(200);
        var act = () => Plan(x => big.Contains(x.Guid), sorted: true);
        act.Should().Throw<NotSupportedException>().WithMessage("*sort, limit or offset*");

        var fits = Ids(50);
        Plan(x => fits.Contains(x.Guid), sorted: true).Should().BeNull();
    }

    [Fact]
    public void A_chunk_still_past_the_ceiling_is_refused()
    {
        var a = Ids(150);
        var b = Ids(120);
        var act = () => Plan(x => a.Contains(x.Guid) && b.Contains(x.Guid));
        act.Should().Throw<NotSupportedException>().WithMessage("*even one chunk*");
    }
}

/// <summary>
/// TASK-834, live against the emulator (gated like <see cref="CosmosLive"/>). The chunk size is lowered to 7 so a
/// list of tens of ids is split; every chunked answer is compared with the same filter run as one query. Measured on
/// the emulator: 13,105 Guids is the largest list one query accepts (SC3020, 524,288 characters), which the past-the-
/// ceiling tests cross with 20,000.
/// </summary>
public sealed class Task834InListChunkingLiveTests : IDisposable
{
    private const int Chunk = 7;
    private readonly CosmosLive _live;
    private readonly ITestOutputHelper _output;
    private readonly List<CosmosClient> _clients = new();

    public Task834InListChunkingLiveTests(ITestOutputHelper output)
    {
        _live = new CosmosLive(output);
        _output = output;
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

    private sealed class ChunkedAsyncStore : AsyncCosmosDBStore<InListDoc>
    {
        private readonly int _chunk;
        public ChunkedAsyncStore(Container container, int chunk) : base(container) => _chunk = chunk;
        protected override int InListChunkSize => _chunk;
    }

    private sealed class ChunkedStore : CosmosDBStore<InListDoc>
    {
        private readonly int _chunk;
        public ChunkedStore(Container container, int chunk) : base(container) => _chunk = chunk;
        protected override int InListChunkSize => _chunk;
    }

    private sealed class Fixture
    {
        public required Container Container { get; init; }
        public required CountingHandler Requests { get; init; }
        public required List<InListDoc> Seed { get; init; }
        public AsyncCosmosDBStore<InListDoc> Async(int chunk = Chunk) => new ChunkedAsyncStore(Container, chunk);
        public CosmosDBStore<InListDoc> Sync(int chunk = Chunk) => new ChunkedStore(Container, chunk);
        /// <summary>The oracle: chunk size far above any list here, so every filter runs as one query.</summary>
        public AsyncCosmosDBStore<InListDoc> Single() => new ChunkedAsyncStore(Container, 1_000_000);
    }

    private async Task<Fixture?> SetUpAsync(int seed = 30)
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
        var docs = Enumerable.Range(0, seed).Select(i => new InListDoc { Guid = Guid.NewGuid(), Name = "n" + i, N = i }).ToList();
        await new ChunkedAsyncStore(container, Chunk).CreateAsync(docs);
        return new Fixture { Container = container, Requests = handler, Seed = docs };
    }

    /// <summary>20 stored ids, 5 that are not stored, and 5 of the stored ones again at the end (another chunk).</summary>
    private static List<Guid?> MixedIds(Fixture f)
    {
        var stored = f.Seed.Take(20).Select(d => d.Guid).ToList();
        return stored.Concat(Enumerable.Range(0, 5).Select(_ => (Guid?)Guid.NewGuid())).Concat(stored.Take(5)).ToList();
    }

    private static List<Guid?> Sorted(IEnumerable<InListDoc> docs) => docs.Select(d => d.Guid).OrderBy(g => g).ToList();

    [Fact]
    public async Task A_chunked_read_returns_what_one_query_returns_async_and_sync()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);
        Expression<Func<InListDoc, bool>> filter = x => x.N >= 0 && ids.Contains(x.Guid);

        var oracle = Sorted(await f.Single().ReadAsync(filter));
        oracle.Should().HaveCount(20);

        var before = f.Requests.Count;
        Sorted(await f.Async().ReadAsync(filter)).Should().Equal(oracle);
        var chunked = f.Requests.Count - before;
        before = f.Requests.Count;
        Sorted(await f.Single().ReadAsync(filter)).Should().Equal(oracle);
        var single = f.Requests.Count - before;
        _output.WriteLine($"requests: chunked {chunked}, one query {single}");
        chunked.Should().BeGreaterThan(single, "the list of 25 distinct ids is read in 4 chunks of 7");

        Sorted(f.Sync().Read(filter, null, null, null)).Should().Equal(oracle);
        (await f.Async().ReadFirstAsync(filter)).Should().NotBeNull();
        f.Sync().ReadFirst(filter).Should().NotBeNull();
    }

    [Fact]
    public async Task A_chunked_count_is_the_one_query_count_with_duplicates_across_chunks()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);
        Expression<Func<InListDoc, bool>> filter = x => ids.Contains(x.Guid);

        var oracle = await f.Single().CountAsync(filter);
        oracle.Should().Be(20);
        (await f.Async().CountAsync(filter)).Should().Be(oracle);
        f.Sync().Count(filter).Should().Be(oracle);
    }

    [Fact]
    public async Task A_chunked_delete_removes_exactly_the_matched_rows_async()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);
        await f.Async().DeleteAsync(x => ids.Contains(x.Guid));
        Sorted(await f.Single().ReadAsync(CancellationToken.None)).Should().Equal(Sorted(f.Seed.Skip(20)));
    }

    [Fact]
    public async Task A_chunked_delete_removes_exactly_the_matched_rows_sync()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);
        f.Sync().Delete(x => ids.Contains(x.Guid) && x.N < 15);
        Sorted(await f.Single().ReadAsync(CancellationToken.None)).Should().Equal(Sorted(f.Seed.Skip(15)));
    }

    [Fact]
    public async Task A_chunked_update_changes_exactly_the_matched_rows_async_and_sync()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);

        await f.Async().UpdateAsync(x => ids.Contains(x.Guid), new PropertyUpdate<InListDoc>().Increment(x => x.N, 1000));
        f.Sync().Update(x => ids.Contains(x.Guid) && x.N >= 1010, d => d.Name = "sync");

        var all = (await f.Single().ReadAsync(CancellationToken.None)).ToDictionary(d => d.Guid!.Value);
        foreach (var seed in f.Seed)
        {
            var stored = all[seed.Guid!.Value];
            var matched = seed.N < 20;
            stored.N.Should().Be(matched ? seed.N + 1000 : seed.N, "each matched row is incremented exactly once");
            stored.Name.Should().Be(matched && seed.N >= 10 ? "sync" : seed.Name);
        }
    }

    [Fact]
    public async Task A_write_failing_in_a_later_chunk_names_the_chunks_applied_and_keeps_them()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = f.Seed.Take(20).Select(d => d.Guid).ToList();   // chunks of 7: [0..6] [7..13] [14..19]
        var poison = ids[15];

        var act = () => f.Async().UpdateAsync(x => ids.Contains(x.Guid), d =>
        {
            if (d.Guid == poison) throw new InvalidOperationException("injected");
            d.Name = "done";
        });
        var ex = (await act.Should().ThrowAsync<CosmosChunkedWriteException>()).Which;
        ex.ChunksApplied.Should().Be(2);
        ex.ChunkCount.Should().Be(3);
        ex.InnerException.Should().BeOfType<InvalidOperationException>();

        var all = (await f.Single().ReadAsync(CancellationToken.None)).ToDictionary(d => d.Guid!.Value);
        ids.Take(14).Should().OnlyContain(g => all[g!.Value].Name == "done", "applied chunks are not rolled back (the user's decision)");

        // A failure in the first chunk is reported as itself: nothing past an unchunked write's damage was applied.
        var first = ids[0];
        var sync = () => f.Sync().Update(x => ids.Contains(x.Guid), d => { if (d.Guid == first) throw new InvalidOperationException("first"); });
        sync.Should().Throw<InvalidOperationException>().WithMessage("first");

        // The sync twin reports a later chunk the same way.
        var later = ids[8];
        var syncLater = () => f.Sync().Update(x => ids.Contains(x.Guid), d => { if (d.Guid == later) throw new InvalidOperationException("later"); });
        syncLater.Should().Throw<CosmosChunkedWriteException>().Which.ChunksApplied.Should().Be(1);
    }

    private static List<Guid?> PastCeiling(Fixture f, int n = 20_000)
        => f.Seed.Select(d => d.Guid).Concat(Enumerable.Range(0, n - f.Seed.Count).Select(_ => (Guid?)Guid.NewGuid())).ToList();

    [Fact]
    public async Task A_list_past_the_measured_ceiling_is_read_counted_and_deleted_in_default_chunks()
    {
        var f = await SetUpAsync(10);
        if (f == null) return;
        var ids = PastCeiling(f);

        // The ceiling this crosses, as one query: SC3020 from the server.
        var single = () => f.Single().ReadAsync(x => ids.Contains(x.Guid));
        (await single.Should().ThrowAsync<CosmosException>()).Which.Message.Should().Contain("SC3020");

        var store = new AsyncCosmosDBStore<InListDoc>(f.Container);
        Sorted(await store.ReadAsync(x => ids.Contains(x.Guid))).Should().Equal(Sorted(f.Seed));
        (await store.CountAsync(x => ids.Contains(x.Guid))).Should().Be(10);
        new CosmosDBStore<InListDoc>(f.Container).Count(x => ids.Contains(x.Guid)).Should().Be(10);
        await store.DeleteAsync(x => ids.Contains(x.Guid));
        (await store.CountAsync()).Should().Be(0);
    }

    public static IEnumerable<object[]> Refusals()
    {
        yield return new object[] { "OR" };
        yield return new object[] { "NOT IN" };
        yield return new object[] { "sort" };
        yield return new object[] { "limit" };
        yield return new object[] { "offset" };
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task An_unsplittable_list_past_the_ceiling_is_refused_before_any_request(string shape)
    {
        var f = await SetUpAsync(5);
        if (f == null) return;
        var ids = PastCeiling(f);
        var store = f.Async(1000);
        var sync = f.Sync(1000);
        Expression<Func<InListDoc, bool>> filter = shape switch
        {
            "OR" => x => ids.Contains(x.Guid) || x.N == 1,
            "NOT IN" => x => !ids.Contains(x.Guid),
            _ => x => ids.Contains(x.Guid),
        };
        var orderBy = shape == "sort" ? OrderBy<InListDoc>.By(x => x.N) : null;
        int? limit = shape == "limit" ? 5 : null;
        int? offset = shape == "offset" ? 1 : null;

        var before = f.Requests.Count;
        var read = () => store.ReadAsync(filter, orderBy, limit, offset);
        var ex = (await read.Should().ThrowAsync<NotSupportedException>()).Which;
        ex.Message.Should().Contain("20000 distinct values").And.Contain("524288").And.NotContain(ids[0].ToString()!);
        _output.WriteLine(ex.Message);
        sync.Invoking(s => s.Read(filter, orderBy, limit, offset).ToList()).Should().Throw<NotSupportedException>();

        if (orderBy == null && limit == null && offset == null)
        {
            await store.Invoking(s => s.CountAsync(filter)).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB count of*");
            sync.Invoking(s => s.Count(filter)).Should().Throw<NotSupportedException>().WithMessage("Cosmos DB count of*");
            await store.Invoking(s => s.DeleteAsync(filter)).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB delete of*");
            sync.Invoking(s => s.Delete(filter)).Should().Throw<NotSupportedException>().WithMessage("Cosmos DB delete of*");
            await store.Invoking(s => s.UpdateAsync(filter, d => d.Name = "x")).Should().ThrowAsync<NotSupportedException>().WithMessage("Cosmos DB update of*");
            sync.Invoking(s => s.Update(filter, d => d.Name = "x")).Should().Throw<NotSupportedException>().WithMessage("Cosmos DB update of*");
        }
        f.Requests.Count.Should().Be(before, "a refusal is decided before any request");
        (await f.Single().CountAsync()).Should().Be(5, "nothing was deleted");
    }

    [Fact]
    public async Task A_sorted_read_past_the_chunk_size_but_within_the_ceiling_is_one_query_that_works()
    {
        var f = await SetUpAsync();
        if (f == null) return;
        var ids = MixedIds(f);
        var sorted = (await f.Async().ReadAsync(x => ids.Contains(x.Guid), OrderBy<InListDoc>.ByDescending(x => x.N), 5, 2)).ToList();
        sorted.Select(d => d.N).Should().Equal(17, 16, 15, 14, 13);
        var or = await f.Async().ReadAsync(x => ids.Contains(x.Guid) || x.N == 25);
        or.Should().HaveCount(21);
    }

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        _live.Dispose();
    }
}
