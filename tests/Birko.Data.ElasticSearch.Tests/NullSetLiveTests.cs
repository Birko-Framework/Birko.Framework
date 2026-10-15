using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class NullableDoc : AbstractModel
{
    public string? Note { get; set; }
    public int? Score { get; set; }
    public DateTime? Seen { get; set; }
}

/// <summary>
/// TASK-501, live: a null Set through the native update stores null. It stored <c>""</c>, so a string read back empty
/// rather than null, and a nullable number or date was handed a string.
/// </summary>
public sealed class NullSetLiveTests : IDisposable
{
    private static readonly HttpClient Http = new();
    private readonly ElasticLive _live;
    public NullSetLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    [Fact]
    public async Task A_null_Set_stores_null_for_a_string_a_number_and_a_date()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<NullableDoc>();
        var settings = _live.NewSettings();
        store.SetSettings(settings);
        var guid = await store.CreateAsync(new NullableDoc { Note = "n", Score = 5, Seen = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        ElasticLive.Refresh();

        await store.UpdateAsync(x => x.Guid == guid,
            new PropertyUpdate<NullableDoc>().Set(x => x.Note, null).Set(x => x.Score, null).Set(x => x.Seen, null));

        var row = (await store.ReadRealtimeAsync(guid))!;
        row.Note.Should().BeNull();
        row.Score.Should().BeNull();
        row.Seen.Should().BeNull();

        // The stored source itself, not only what the client deserialises: "" would also read back as a non-null string.
        var source = await Http.GetStringAsync($"{Environment.GetEnvironmentVariable("BIRKO_ES_URL")!.TrimEnd('/')}/{store.GetIndexName()}/_doc/{guid}");
        source.Should().Contain("\"note\":null").And.NotContain("\"note\":\"\"");
        source.Should().Contain("\"score\":null").And.Contain("\"seen\":null");
    }
}
