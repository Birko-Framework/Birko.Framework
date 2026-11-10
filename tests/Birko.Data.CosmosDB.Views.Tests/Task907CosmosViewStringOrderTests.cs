using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-907, live against the Cosmos emulator: a view's string MIN/MAX is Ordinal. Cosmos DB orders strings by code point,
/// which is Ordinal but for a group holding both a character above U+FFFF and one in U+E000–U+FFFF (UTF-16 puts the
/// surrogate pair first): such a group is refused by name, every other group is answered exactly. Measured before: the
/// mixed group answered U+FF5E as its MIN.
/// </summary>
public sealed class Task907CosmosViewStringOrderTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private CosmosClient? _client;
    private Database? _db;
    private Container? _rows;

    public Task907CosmosViewStringOrderTests(ITestOutputHelper output) => _output = output;

    public class Row
    {
        public string? id { get; set; }
        public string? Grp { get; set; }
        public string? S { get; set; }
    }

    public class Text
    {
        public string? Grp { get; set; }
        public string? Low { get; set; }
        public string? High { get; set; }
    }

    private static readonly (string Grp, string S)[] Seed =
    {
        ("mixed", "\U0001F600"), ("mixed", "～"),
        ("astral", "\U0001F600"), ("astral", "\U0001F4A9"),
        ("bmp", "～"), ("bmp", ""),
        ("plain", "Banana"), ("plain", "apple"), ("plain", "a\t"), ("plain", ""),
    };

    public async Task InitializeAsync()
    {
        var conn = Environment.GetEnvironmentVariable("BIRKO_COSMOS_CONNECTION");
        if (string.IsNullOrWhiteSpace(conn))
        {
            _output.WriteLine("SKIPPED: no live Cosmos. Set BIRKO_COSMOS_CONNECTION.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
                throw new InvalidOperationException("BIRKO_REQUIRE_LIVE is set but BIRKO_COSMOS_CONNECTION is not.");
            return;
        }
        _client = new CosmosClient(conn, new CosmosClientOptions { ConnectionMode = ConnectionMode.Gateway });
        _db = (await _client.CreateDatabaseIfNotExistsAsync("birko_task907v_" + Guid.NewGuid().ToString("N"))).Database;
        _rows = (await _db.CreateContainerIfNotExistsAsync("Rows", "/id")).Container;
        foreach (var (grp, s) in Seed)
        {
            var row = new Row { id = Guid.NewGuid().ToString(), Grp = grp, S = s };
            await _rows.CreateItemAsync(row, new PartitionKey(row.id));
        }
    }

    public async Task DisposeAsync()
    {
        if (_db != null) { try { await _db.DeleteAsync(); } catch { /* best-effort */ } }
        _client?.Dispose();
    }

    private static ViewDefinition View() => new ViewDefinitionBuilder<Text>()
        .From<Row>()
        .GroupBy<Row, string?>(r => r.Grp)
        .Select<Row, string?>(r => r.Grp, v => v.Grp)
        .Min<Row, string?>(r => r.S, v => v.Low)
        .Max<Row, string?>(r => r.S, v => v.High)
        .Build();

    [Theory]
    [InlineData("astral")]
    [InlineData("bmp")]
    [InlineData("plain")]
    public async Task A_group_without_both_kinds_is_answered_in_Ordinal_order(string group)
    {
        if (_rows == null) return;
        var values = Seed.Where(s => s.Grp == group).Select(s => s.S).ToList();

        var row = (await new CosmosViewStore<Text>(_rows, View()).QueryAsync(v => v.Grp == group)).Single();

        (row.Low, row.High).Should().Be((values.Min(StringComparer.Ordinal), values.Max(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task A_group_mixing_an_astral_character_with_U_E000_to_U_FFFF_is_refused_by_name()
    {
        if (_rows == null) return;

        var act = () => new CosmosViewStore<Text>(_rows, View()).QueryAsync(v => v.Grp == "mixed");

        (await act.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("Row.S").And.Contain("U+FFFF");
    }
}
