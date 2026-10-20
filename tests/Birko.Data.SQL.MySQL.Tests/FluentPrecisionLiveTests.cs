using System;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-692, live: a fluent <c>HasPrecision</c> / <c>HasScale</c> and <c>HasMaxLength</c> are what the table holds, read
/// back from <c>information_schema.columns</c> (rule 63). Before, the decimal stored <c>(22,6)</c> whatever the mapping
/// said, and a string's <c>HasPrecision(n)</c> was <c>LONGTEXT</c>.
/// </summary>
public class FluentPrecisionLiveTests
{
    private const string Table = "T692Measures";

    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    private readonly ITestOutputHelper _output;
    public FluentPrecisionLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return false;
    }

    public class Measure : AbstractLogModel
    {
        public decimal WeightKg { get; set; }
        public decimal? WaistCm { get; set; }
        public string? Note { get; set; }
    }

    private sealed class MeasureMap : IModelMapping<Measure>
    {
        public void Configure(ModelMap<Measure> map)
        {
            map.ToTable(Table);
            map.Property(x => x.WeightKg).HasPrecision(6).HasScale(2);
            map.Property(x => x.WaistCm).HasPrecision(5).HasScale(1);
            map.Property(x => x.Note).HasMaxLength(2000);
        }
    }

    [Fact]
    public async Task The_table_holds_the_fluent_precision_scale_and_length()
    {
        if (!RequireServer()) return;
        var registry = new ModelMapRegistry();
        registry.Register(new MeasureMap());
        registry.ApplyToDatabase();
        var store = new AsyncMySQLStore<Measure>();
        store.SetSettings(Settings());
        try { store.Connector!.DropTable(new[] { typeof(Measure) }); } catch { }

        await store.CreateAsync(new Measure { WeightKg = 82.35m, WaistCm = 91.4m, Note = "ok" });

        await using var connection = new MySqlConnection(Settings().GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COLUMN_NAME, DATA_TYPE, NUMERIC_PRECISION, NUMERIC_SCALE, CHARACTER_MAXIMUM_LENGTH "
            + "FROM information_schema.columns WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t";
        command.Parameters.AddWithValue("@t", Table);
        var columns = new System.Collections.Generic.Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns[reader.GetString(0)] = $"{reader.GetString(1)}"
                    + $"({(reader.IsDBNull(2) ? "" : reader.GetValue(2))},{(reader.IsDBNull(3) ? "" : reader.GetValue(3))},{(reader.IsDBNull(4) ? "" : reader.GetValue(4))})";
            }
        }
        foreach (var (name, type) in columns) _output.WriteLine($"{name}: {type}");

        columns["WeightKg"].Should().Be("decimal(6,2,)");
        columns["WaistCm"].Should().Be("decimal(5,1,)");
        columns["Note"].Should().Be("varchar(,,2000)");
        store.Connector!.DropTable(new[] { typeof(Measure) });
    }
}
