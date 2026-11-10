using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public enum AggKind { Low, Mid, High }

public class AggSub
{
    public int X { get; set; }
}

public class AggDoc : AbstractModel
{
    public string? Bucket { get; set; }
    [Keyword] public string? Code { get; set; }
    [Text] public string? Title { get; set; }
    public string? Opt { get; set; }
    public int A { get; set; }
    public int? N { get; set; }
    public double D { get; set; }
    public long L { get; set; }
    public decimal M { get; set; }
    public DateTime At { get; set; }
    public DateTime? Seen { get; set; }
    public bool Flag { get; set; }
    public AggKind Kind { get; set; }
    public Guid Ref { get; set; }
    public AggSub? Sub { get; set; }
}

/// <summary>One seeded index shared by every case of <see cref="Task878AggregateLiveTests"/>.</summary>
public sealed class Task878AggregateFixture : IDisposable
{
    private static readonly HttpClient Http = new();
    private static string? Url => Environment.GetEnvironmentVariable("BIRKO_ES_URL");

    public static readonly Guid RefOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid RefTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public IReadOnlyList<AggDoc> Rows { get; }
    public ElasticSearchStore<AggDoc>? Sync { get; }
    public AsyncElasticSearchStore<AggDoc>? Async { get; }
    public Birko.Data.ElasticSearch.Stores.Settings? Settings { get; }
    private readonly string? _prefix;

    public Task878AggregateFixture()
    {
        static DateTime T(int day, int hour) => new(2026, 3, day, hour, 15, 0, DateTimeKind.Utc);
        Rows = new List<AggDoc>
        {
            new() { Bucket = "alpha", Code = "C1", Title = "one two", Opt = "x", A = 1, N = 10, D = 1.5, L = 100, M = 1.25m, At = T(1, 1), Seen = T(1, 2), Flag = true, Kind = AggKind.Low, Ref = RefOne, Sub = new AggSub { X = 1 } },
            new() { Bucket = "alpha", Code = "C2", Title = "two", Opt = null, A = 2, N = null, D = 2.5, L = 200, M = 2.5m, At = T(1, 5), Seen = null, Flag = false, Kind = AggKind.Mid, Ref = RefTwo, Sub = new AggSub { X = 2 } },
            new() { Bucket = "Alpha", Code = "C1", Title = "three", Opt = "y", A = 3, N = 30, D = -1, L = 300, M = 3m, At = T(2, 1), Seen = T(2, 3), Flag = true, Kind = AggKind.High, Ref = RefOne, Sub = null },
            new() { Bucket = "beta", Code = "C2", Title = "four", Opt = "x", A = 4, N = null, D = 0.25, L = 400, M = 4m, At = T(2, 7), Seen = null, Flag = true, Kind = AggKind.Low, Ref = RefTwo, Sub = new AggSub { X = 4 } },
            new() { Bucket = "beta", Code = "C3", Title = "five", Opt = null, A = 5, N = 50, D = 5, L = 500, M = 5m, At = T(3, 1), Seen = T(3, 1), Flag = false, Kind = AggKind.Mid, Ref = RefOne, Sub = null },
            new() { Bucket = "gamma two", Code = "C3", Title = "six", Opt = "z", A = 6, N = null, D = 6, L = 600, M = 6m, At = T(3, 9), Seen = null, Flag = false, Kind = AggKind.High, Ref = RefTwo, Sub = new AggSub { X = 6 } },
            new() { Bucket = null, Code = null, Title = null, Opt = "x", A = 7, N = 70, D = 7, L = 700, M = 7m, At = T(3, 12), Seen = T(3, 13), Flag = true, Kind = AggKind.Low, Ref = RefOne, Sub = null },
        };

        if (string.IsNullOrWhiteSpace(Url)) return;
        _prefix = "birko878" + Guid.NewGuid().ToString("N");
        var settings = Settings = new Birko.Data.ElasticSearch.Stores.Settings { Location = Url!, Name = _prefix };
        Sync = new ElasticSearchStore<AggDoc>();
        Sync.SetSettings(settings);
        Async = new AsyncElasticSearchStore<AggDoc>();
        Async.SetSettings(settings);
        foreach (var row in Rows) Sync.Create(row);
        ElasticLive.Refresh();
    }

    public void Dispose()
    {
        if (_prefix == null) return;
        try { Http.DeleteAsync(Url!.TrimEnd('/') + "/" + _prefix + "*").GetAwaiter().GetResult(); } catch { }
    }
}

/// <summary>
/// TASK-878, live: an Elasticsearch store aggregate answers what the LINQ aggregation (<see cref="AggregateHelper"/>, the
/// in-memory stores' implementation) answers over the same rows. Names are property names, as on SQL and in memory,
/// and the stored field name is accepted too. A group-by on the C# name returned no groups, and a Count read 0.
/// </summary>
public sealed class Task878AggregateLiveTests : IClassFixture<Task878AggregateFixture>, IDisposable
{
    private readonly Task878AggregateFixture _fixture;
    private readonly ElasticLive _live;
    private readonly ITestOutputHelper _output;

    public Task878AggregateLiveTests(Task878AggregateFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
        _live = new ElasticLive(output);
    }

    public void Dispose() => _live.Dispose();

    private static AggregateField Count(string source = "A", string? alias = "n") => new(AggregateFunction.Count, source, alias);
    private static AggregateField Sum(string source, string? alias = null) => new(AggregateFunction.Sum, source, alias);
    private static AggregateField Avg(string source, string? alias = null) => new(AggregateFunction.Avg, source, alias);
    private static AggregateField Min(string source, string? alias = null) => new(AggregateFunction.Min, source, alias);
    private static AggregateField Max(string source, string? alias = null) => new(AggregateFunction.Max, source, alias);

    private static readonly AggregateField[] Numeric =
    {
        Count(), Sum("A"), Avg("A"), Min("A"), Max("A"),
        Sum("N"), Avg("N"), Min("N"), Max("N"),
        Sum("D"), Avg("D"), Min("D"), Max("D"),
        Sum("L"), Min("L"), Max("L"),
        Sum("M"), Avg("M"),
    };

    public static readonly Dictionary<string, AggregateQuery<AggDoc>> Cases = new()
    {
        ["global count"] = new() { Aggregates = new[] { Count() } },
        ["global count, source unknown"] = new() { Aggregates = new[] { Count("Nope") } },
        ["global count, source lower-case"] = new() { Aggregates = new[] { Count("a") } },
        ["global count, default alias"] = new() { Aggregates = new[] { Count("A", null) } },
        ["global numeric metrics"] = new() { Aggregates = Numeric },
        ["global metrics by field name"] = new() { Aggregates = new[] { Sum("a", "s"), Avg("n", "v"), Max("d", "x") } },
        ["global min/max of a date"] = new() { Aggregates = new[] { Min("At"), Max("At"), Min("Seen"), Max("Seen") } },
        ["global min/max of a bool"] = new() { Aggregates = new[] { Min("Flag"), Max("Flag") } },
        ["global with filter"] = new() { Filter = x => x.A > 2, Aggregates = Numeric },
        ["global, filter matches nothing"] = new() { Filter = x => x.A > 100, Aggregates = new[] { Count(), Sum("A") } },
        ["group by property"] = new() { GroupByFields = new[] { "Bucket" }, Aggregates = Numeric },
        ["group by field name"] = new() { GroupByFields = new[] { "bucket" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group by keyword member"] = new() { GroupByFields = new[] { "Code" }, Aggregates = new[] { Count(), Sum("A"), Avg("N") } },
        ["group by member with nulls"] = new() { GroupByFields = new[] { "Opt" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group by int"] = new() { GroupByFields = new[] { "A" }, Aggregates = new[] { Count(), Sum("D") } },
        ["group by nullable int"] = new() { GroupByFields = new[] { "N" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group by double"] = new() { GroupByFields = new[] { "D" }, Aggregates = new[] { Count() } },
        ["group by bool"] = new() { GroupByFields = new[] { "Flag" }, Aggregates = new[] { Count(), Sum("A"), Min("At") } },
        ["group by enum"] = new() { GroupByFields = new[] { "Kind" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group by guid"] = new() { GroupByFields = new[] { "Ref" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group by date"] = new() { GroupByFields = new[] { "At" }, Aggregates = new[] { Count() } },
        ["group by nullable date"] = new() { GroupByFields = new[] { "Seen" }, Aggregates = new[] { Count() } },
        ["group by two members"] = new() { GroupByFields = new[] { "Bucket", "Code" }, Aggregates = new[] { Count(), Sum("A"), Max("N") } },
        ["group by two members with nulls"] = new() { GroupByFields = new[] { "Opt", "Flag" }, Aggregates = new[] { Count(), Avg("D") } },
        ["group, all-null sum"] = new() { GroupByFields = new[] { "Code" }, Aggregates = new[] { Sum("N"), Min("N"), Avg("N") } },
        ["group with filter"] = new() { Filter = x => x.A > 2, GroupByFields = new[] { "Bucket" }, Aggregates = new[] { Count(), Sum("A") } },
        ["group, filter matches nothing"] = new() { Filter = x => x.A > 100, GroupByFields = new[] { "Bucket" }, Aggregates = new[] { Count() } },
        ["group, ordered and paged"] = new()
        {
            GroupByFields = new[] { "Code" }, Aggregates = new[] { Sum("A", "total") },
            OrderBy = new Dictionary<string, bool> { ["total"] = true }, Limit = 2
        },
        ["time bucket"] = new() { TimeColumn = "At", TimeBucketInterval = "1 day", Aggregates = new[] { Count(), Sum("A") } },
        ["time bucket by field name"] = new() { TimeColumn = "at", TimeBucketInterval = "1 day", Aggregates = new[] { Count() } },
        ["time bucket, hourly"] = new() { TimeColumn = "At", TimeBucketInterval = "01:00:00", Aggregates = new[] { Count() } },
        ["time bucket, nullable column"] = new() { TimeColumn = "Seen", TimeBucketInterval = "1 day", Aggregates = new[] { Count(), Sum("A") } },
        ["time bucket and group"] = new() { TimeColumn = "At", TimeBucketInterval = "1 day", GroupByFields = new[] { "Bucket" }, Aggregates = new[] { Count(), Max("A") } },
        ["time bucket and two groups"] = new() { TimeColumn = "At", TimeBucketInterval = "1 day", GroupByFields = new[] { "Flag", "Opt" }, Aggregates = new[] { Count() } },
    };

    public static IEnumerable<object[]> CaseNames()
        => Cases.Keys.SelectMany(k => new[] { new object[] { k, false }, new object[] { k, true } });

    private IReadOnlyList<AggregateResult> Run(AggregateQuery<AggDoc> query, bool async)
        => async ? _fixture.Async!.AggregateAsync(query).GetAwaiter().GetResult() : _fixture.Sync!.Aggregate(query);

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void An_aggregate_answers_what_LINQ_answers(string name, bool async)
    {
        if (!_live.Ready()) return;
        var query = Cases[name];
        var expected = Normalise(AggregateHelper.LinqAggregate(_fixture.Rows, query), query.OrderBy != null);

        var actual = Normalise(Run(query, async), query.OrderBy != null);

        _output.WriteLine("expected:\n  " + string.Join("\n  ", expected));
        _output.WriteLine("actual:\n  " + string.Join("\n  ", actual));
        actual.Should().Equal(expected);
    }

    public static readonly Dictionary<string, AggregateQuery<AggDoc>> Refusals = new()
    {
        ["group by unknown"] = new() { GroupByFields = new[] { "Nope" }, Aggregates = new[] { Count() } },
        ["group by nested member"] = new() { GroupByFields = new[] { "Sub.X" }, Aggregates = new[] { Count() } },
        ["sum of unknown"] = new() { Aggregates = new[] { Sum("Nope") } },
        ["sum of nested member"] = new() { Aggregates = new[] { Sum("Sub.X") } },
        ["time column unknown"] = new() { TimeColumn = "Nope", TimeBucketInterval = "1 day", Aggregates = new[] { Count() } },
        ["interval without column"] = new() { TimeBucketInterval = "1 day", Aggregates = new[] { Count() } },
        ["column without interval"] = new() { TimeColumn = "At", Aggregates = new[] { Count() } },
        ["interval that does not parse"] = new() { TimeColumn = "At", TimeBucketInterval = "soon", Aggregates = new[] { Count() } },
    };

    public static IEnumerable<object[]> RefusalNames()
        => Refusals.Keys.SelectMany(k => new[] { new object[] { k, false }, new object[] { k, true } });

    /// <summary>A name LINQ refuses with <see cref="ArgumentException"/> is refused the same way, naming it.</summary>
    [Theory]
    [MemberData(nameof(RefusalNames))]
    public void A_name_LINQ_refuses_is_refused(string name, bool async)
    {
        if (!_live.Ready()) return;
        var query = Refusals[name];
        ((Action)(() => AggregateHelper.LinqAggregate(_fixture.Rows, query))).Should().Throw<ArgumentException>();

        var act = () => Run(query, async);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// What LINQ computes and Elasticsearch cannot: a sum or average of a string is refused by LINQ too; a min or max of a
    /// string in an order other than Ordinal (TASK-907: Ordinal, the default, is answered from the keyword); a group-by on a
    /// [Text] member has no exact form.
    /// </summary>
    [Theory]
    [InlineData("Bucket", AggregateFunction.Min, false)]
    [InlineData("Bucket", AggregateFunction.Max, true)]
    [InlineData("Ref", AggregateFunction.Min, false)]
    [InlineData("Bucket", AggregateFunction.Sum, true)]
    [InlineData("Code", AggregateFunction.Avg, false)]
    public void A_metric_over_a_string_is_refused_by_name(string source, AggregateFunction function, bool async)
    {
        if (!_live.Ready()) return;
        var query = new AggregateQuery<AggDoc>
        {
            Aggregates = new[] { new AggregateField(function, source) },
            StringComparison = StringComparison.CurrentCulture,
        };

        var act = () => Run(query, async);

        act.Should().Throw<NotSupportedException>().WithMessage($"*{source}*");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_group_by_a_text_mapped_member_is_refused_by_name(bool async)
    {
        if (!_live.Ready()) return;
        var query = new AggregateQuery<AggDoc> { GroupByFields = new[] { "Title" }, Aggregates = new[] { Count() } };

        var act = () => Run(query, async);

        act.Should().Throw<NotSupportedException>().WithMessage("*Title*");
    }

    /// <summary>
    /// Shapes LINQ answers and Elasticsearch cannot: a group-by on an object member, a time bucket on a member that is not
    /// a DateTime, and an interval that does not divide the time from 0001-01-01 to the Unix epoch (a week: LINQ's weeks
    /// start on a Monday, Elasticsearch's on a Thursday).
    /// </summary>
    [Theory]
    [InlineData("group by an object member", false)]
    [InlineData("time bucket on a number", true)]
    [InlineData("weekly time bucket", false)]
    [InlineData("weekly time bucket", true)]
    public void A_shape_Elasticsearch_cannot_answer_as_LINQ_does_is_refused(string name, bool async)
    {
        if (!_live.Ready()) return;
        var query = name switch
        {
            "group by an object member" => new AggregateQuery<AggDoc> { GroupByFields = new[] { "Sub" }, Aggregates = new[] { Count() } },
            "time bucket on a number" => new AggregateQuery<AggDoc> { TimeColumn = "A", TimeBucketInterval = "1 day", Aggregates = new[] { Count() } },
            _ => new AggregateQuery<AggDoc> { TimeColumn = "At", TimeBucketInterval = "7 days", Aggregates = new[] { Count() } },
        };

        var act = () => Run(query, async);

        act.Should().Throw<NotSupportedException>();
    }

    /// <summary>Every page of groups is read: seven groups through pages of two.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_page_of_groups_is_read(bool async)
    {
        if (!_live.Ready()) return;
        var query = new AggregateQuery<AggDoc> { TimeColumn = "At", TimeBucketInterval = "1 hour", GroupByFields = new[] { "A" }, Aggregates = new[] { Count(), Sum("D") } };
        var expected = Normalise(AggregateHelper.LinqAggregate(_fixture.Rows, query), ordered: false);

        IReadOnlyList<AggregateResult> rows;
        if (async)
        {
            var store = new AsyncElasticSearchStore<AggDoc> { AggregateGroupPageSize = 2 };
            store.SetSettings(_fixture.Settings!);
            rows = await store.AggregateAsync(query);
        }
        else
        {
            var store = new ElasticSearchStore<AggDoc> { AggregateGroupPageSize = 2 };
            store.SetSettings(_fixture.Settings!);
            rows = store.Aggregate(query);
        }

        expected.Should().HaveCount(7);
        Normalise(rows, ordered: false).Should().Equal(expected);
    }

    /// <summary>
    /// A dynamically mapped string is grouped on its keyword sub-field, which does not index a value past ignore_above
    /// (256): such a row would be counted in the null group. It is refused instead.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_group_holding_a_value_its_keyword_did_not_index_is_refused(bool async)
    {
        if (!_live.Ready()) return;
        var settings = _live.NewSettings();
        var store = new AsyncElasticSearchStore<AggDoc>();
        store.SetSettings(settings);
        store.CreateAsync(new AggDoc { Bucket = new string('b', 300), A = 1 }).GetAwaiter().GetResult();
        store.CreateAsync(new AggDoc { Bucket = "short", A = 2 }).GetAwaiter().GetResult();
        store.CreateAsync(new AggDoc { Bucket = null, A = 3 }).GetAwaiter().GetResult();
        ElasticLive.Refresh();
        var query = new AggregateQuery<AggDoc> { GroupByFields = new[] { "Bucket" }, Aggregates = new[] { Count() } };

        Func<IReadOnlyList<AggregateResult>> act = async
            ? () => store.AggregateAsync(query).GetAwaiter().GetResult()
            : () => { var sync = new ElasticSearchStore<AggDoc>(); sync.SetSettings(settings); return sync.Aggregate(query); };

        act.Should().Throw<InvalidOperationException>().WithMessage("*Bucket*ignore_above*");
    }

    private static List<string> Normalise(IReadOnlyList<AggregateResult> rows, bool ordered)
    {
        var list = rows
            .Select(r => string.Join(", ", r.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + Value(kv.Value))))
            .ToList();
        if (!ordered) list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static string Value(object? value) => value switch
    {
        null => "null",
        string s => "'" + s + "'",
        bool b => b ? "true" : "false",
        DateTime t => (t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : t).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        Guid g => "guid:" + g.ToString("D"),
        Enum e => "enum:" + e,
        IConvertible c => Math.Round(c.ToDouble(CultureInfo.InvariantCulture), 9).ToString("R", CultureInfo.InvariantCulture),
        _ => value.GetType().Name + ":" + value
    };
}
