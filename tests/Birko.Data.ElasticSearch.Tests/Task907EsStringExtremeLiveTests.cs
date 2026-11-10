using System;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-907, live Elasticsearch: a string Min / Max is the first <c>terms</c> bucket of the member's keyword. A value its
/// <c>.keyword</c> sub-field skipped (past <c>ignore_above</c>, 256 by default) has no bucket, so it would be left out
/// silently; the read fails instead, as a group key over such a value fails (TASK-878). Gated on <c>BIRKO_ES_URL</c>.
/// </summary>
public sealed class Task907EsStringExtremeLiveTests : IDisposable
{
    private readonly ElasticLive _live;
    public Task907EsStringExtremeLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    [Theory]
    [InlineData(AggregateFunction.Min)]
    [InlineData(AggregateFunction.Max)]
    public async Task A_value_past_the_keyword_limit_fails_the_extreme_rather_than_being_left_out(AggregateFunction function)
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new StringOrderDoc { Kind = "a", Name = "m" });
        await store.CreateAsync(new StringOrderDoc { Kind = "a", Name = new string(function == AggregateFunction.Min ? 'A' : 'z', 300) });
        ElasticLive.Refresh();

        var act = () => store.AggregateAsync(new AggregateQuery<StringOrderDoc>
        {
            Aggregates = new[] { new AggregateField(function, "Name", "x") },
        });

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("ignore_above");
    }

    [Fact]
    public async Task Values_within_the_keyword_limit_answer_the_extremes()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<StringOrderDoc>();
        store.SetSettings(_live.NewSettings());
        await store.CreateAsync(new StringOrderDoc { Kind = "a", Name = "m" });
        await store.CreateAsync(new StringOrderDoc { Kind = "a", Name = new string('z', 256) });
        await store.CreateAsync(new StringOrderDoc { Kind = "a", Name = null });
        ElasticLive.Refresh();

        var row = (await store.AggregateAsync(new AggregateQuery<StringOrderDoc>
        {
            Aggregates = new[] { new AggregateField(AggregateFunction.Min, "Name", "min"), new AggregateField(AggregateFunction.Max, "Name", "max") },
        })).Should().ContainSingle().Subject;

        row.GetValue<string>("min").Should().Be("m");
        row.GetValue<string>("max").Should().Be(new string('z', 256));
    }
}
