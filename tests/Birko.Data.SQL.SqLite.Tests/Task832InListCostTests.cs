using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-832 (FIELD-051, reported by Symbio): <c>ReadAsync(x =&gt; ids.Contains(x.Guid))</c> grew 7–9× per doubling of
/// <c>ids</c>. The measurement splits the read into its stages so the super-linear one is named before anything
/// is fixed.
/// </summary>
public class Task832InListCostTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-t832-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _out;

    public Task832InListCostTests(ITestOutputHelper output)
    {
        _out = output;
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => TestTempDirectory.Delete(_root);

    public class InRow : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class InRowMapping : IModelMapping<InRow>
    {
        public void Configure(ModelMap<InRow> map)
        {
            map.ToTable("T832InRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name);
        }
    }

    private async Task<(AsyncSQLiteStore<InRow> Store, SqLiteSettings Settings, List<Guid> Ids)> SeedAsync(int rows)
    {
        var registry = new ModelMapRegistry();
        registry.Register(new InRowMapping());
        registry.ApplyToDatabase();
        var settings = new SqLiteSettings(_root, $"t832-{Guid.NewGuid():N}.db");
        var store = new AsyncSQLiteStore<InRow>();
        store.SetSettings(settings);
        var items = Enumerable.Range(0, rows).Select(i => new InRow { Guid = Guid.NewGuid(), Name = $"r{i}" }).ToList();
        await store.CreateAsync(items, ct: CancellationToken.None);
        return (store, settings, items.Select(x => x.Guid!.Value).ToList());
    }

    // ── the regression: GetGeneratedQuery, built on every read ─────────────────────────────────────────

    private static System.Data.Common.DbCommand Command(string text, params (string Name, object? Value)[] parameters)
    {
        var command = new Microsoft.Data.Sqlite.SqliteCommand(text);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter(name, value ?? DBNull.Value));
        }
        return command;
    }

    [Fact]
    public void The_diagnostic_query_stays_linear_in_the_parameter_count()
    {
        // Measured before: 2,000 parameters took 6.5 s and 4,000 about 87 s; 20,000 did not finish in 300 s. The bound is
        // generous on purpose; it guards the shape of the cost, not a timing.
        const int n = 20_000;
        var names = Enumerable.Range(0, n).Select(i => $"@WHEREGuid{i}_{i}").ToArray();
        using var command = Command($"SELECT Guid FROM T WHERE Guid IN ({string.Join(", ", names)})",
            names.Select(name => (name, (object?)Guid.NewGuid().ToString())).ToArray());

        var sw = Stopwatch.StartNew();
        var rendered = DataBase.GetGeneratedQuery(command);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        rendered.Should().NotContain("@WHERE");
    }

    [Fact]
    public void The_longest_name_wins_where_one_name_prefixes_another()
    {
        using var command = Command("SELECT 1 WHERE A IN (@WHEREA0_5, @WHEREA0_50) LIMIT @LIMIT OFFSET @LIMITx",
            ("@WHEREA0_5", "five"), ("@WHEREA0_50", "fifty"), ("@LIMIT", 10), ("@LIMITx", 20));

        DataBase.GetGeneratedQuery(command).Should().Be("SELECT 1 WHERE A IN ('five', 'fifty') LIMIT '10' OFFSET '20'");
    }

    [Fact]
    public void A_value_that_contains_a_parameter_name_is_not_rewritten()
    {
        using var command = Command("SELECT 1 WHERE A = @a AND B = @b", ("@a", "@b"), ("@b", "x"));

        DataBase.GetGeneratedQuery(command).Should().Be("SELECT 1 WHERE A = '@b' AND B = 'x'");
    }

    [Fact]
    public void A_command_without_parameters_is_returned_as_is()
    {
        using var command = Command("SELECT 1");

        DataBase.GetGeneratedQuery(command).Should().Be("SELECT 1");
    }

    // ── the measurement (FIELD-051) ─────────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Measurement")]
    public async Task Measure_the_stages_of_an_in_list_read()
    {
        var (store, settings, all) = await SeedAsync(4000);
        var connector = new SqLiteConnector(settings);

        foreach (var n in new[] { 500, 1000, 2000, 4000 })
        {
            var ids = all.Take(n).ToList();
            Expression<Func<InRow, bool>> filter = x => ids.Contains(x.Guid!.Value);

            var sw = Stopwatch.StartNew();
            var conditions = DataBase.ParseConditionExpression(filter).ToList();
            var parse = sw.Elapsed.TotalMilliseconds;

            using var connection = connector.CreateConnection(settings);
            using var command = connection.CreateCommand();
            sw.Restart();
            var sql = connector.ConditionDefinition(conditions.Single(), command);
            var render = sw.Elapsed.TotalMilliseconds;

            // SQLite alone: the same statement shape, parameters named and bound without the framework.
            connection.Open();
            using var raw = connection.CreateCommand();
            raw.CommandText = "SELECT COUNT(*) FROM T832InRows WHERE Guid IN ("
                + string.Join(", ", Enumerable.Range(0, n).Select(i => $"@p{i}")) + ")";
            for (var i = 0; i < n; i++)
            {
                var p = raw.CreateParameter();
                p.ParameterName = $"@p{i}";
                p.Value = ids[i].ToString();
                raw.Parameters.Add(p);
            }
            sw.Restart();
            var rawCount = Convert.ToInt32(raw.ExecuteScalar());
            var rawExec = sw.Elapsed.TotalMilliseconds;

            // The framework's own SELECT, built then executed by hand.
            using var built = connection.CreateCommand();
            sw.Restart();
            connector.CreateSelectCommand(built, new[] { "T832InRows" }, new Dictionary<int, string> { { 0, "Guid" } },
                DataBase.ParseConditionExpression(filter));
            var build = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var builtRows = 0;
            using (var reader = built.ExecuteReader())
            {
                while (reader.Read()) builtRows++;
            }
            var builtExec = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            var diagnostic = DataBase.GetGeneratedQuery(built);
            var generated = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var read = (await store.ReadAsync(filter, ct: CancellationToken.None)).Count();
            var full = sw.Elapsed.TotalMilliseconds;

            _out.WriteLine($"n={n,5}  parse={parse,8:F1} ms  render={render,8:F1} ms ({command.Parameters.Count} params, {sql.Length} chars)  raw-sqlite={rawExec,8:F1} ms ({rawCount})  build-select={build,8:F1} ms  exec-built={builtExec,9:F1} ms ({builtRows})  GetGeneratedQuery={generated,9:F1} ms ({diagnostic.Length} chars)  ReadAsync={full,8:F1} ms ({read})");
            if (n == 500) _out.WriteLine(built.CommandText.Substring(0, Math.Min(300, built.CommandText.Length)));
            read.Should().Be(n);
        }
    }
}
