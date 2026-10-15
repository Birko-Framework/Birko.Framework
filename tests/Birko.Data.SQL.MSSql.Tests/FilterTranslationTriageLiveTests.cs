using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-153 on SQL Server: the renderings that are dialect-sensitive, checked against a real server. A bool column as a
/// predicate (<c>(col = '1')</c>, SH-M135/SH-M136), LIKE metacharacters made literal with <c>ESCAPE '!'</c> (SH-M134),
/// and a value on the left of a comparison (SH-M123).
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class FilterTranslationTriageLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MSSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MSSQL_PORT"), out var p) ? p : 1433;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MSSQL_USER") ?? "sa";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MSSQL_PASSWORD") ?? "Birko!Passw0rd";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MSSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public FilterTranslationTriageLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live SQL Server. Set BIRKO_MSSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MSSqlSettings Settings() => new(Host!, Database, User, Password, Port)
    {
        TrustServerCertificate = true
    };

    private static void Exec(string sql)
    {
        using var connection = new SqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("MsFtTriage")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public int? Score { get; set; }
        public bool Vip { get; set; }
        public int Premium { get; set; }
    }

    private static Row[] Seed() => new Row[]
    {
        new() { Name = "A_1", Age = 10, Score = 5, Vip = true, Premium = 200 },
        new() { Name = "A21", Age = 20, Score = null, Vip = false, Premium = 50 },
        new() { Name = "100% off", Age = 30, Score = 150, Vip = false, Premium = 10 },
        new() { Name = "[x]", Age = 40, Score = 1, Vip = true, Premium = 90 },
        new() { Name = "x", Age = 50, Score = 2, Vip = false, Premium = 0 },
    };

    private MSSqlConnector Seeded()
    {
        Exec("DROP TABLE IF EXISTS [MsFtTriage]");
        var connector = new MSSqlConnector(Settings());
        connector.CreateTable(new[] { typeof(Row) });
        foreach (var row in Seed())
        {
            row.Guid = Guid.NewGuid();
            connector.Insert(row);
        }
        return connector;
    }

    private static int[] Ages(IEnumerable<Row> rows) => rows.Select(r => r.Age).OrderBy(a => a).ToArray();

    private void Agrees(Expression<Func<Row, bool>> filter)
        => Ages(Seeded().Select(typeof(Row), filter).Cast<Row>()).Should().Equal(Ages(Seed().Where(filter.Compile())), filter.ToString());

    [Fact]
    public void A_bool_column_tests_a_CASE_in_WHERE()
    {
        if (!RequireServer()) return;
        Agrees(x => (x.Vip ? x.Premium : x.Score) > 100);
        Agrees(x => (!x.Vip ? x.Premium : 0) > 20);
    }

    [Fact]
    public void A_bool_column_tests_a_CASE_in_an_UPDATE_SET()
    {
        if (!RequireServer()) return;
        var connector = Seeded();
        connector.Update<Row, int>(typeof(Row),
            new Dictionary<Expression<Func<Row, int>>, Expression<Func<Row, int>>> { [x => x.Premium] = x => x.Vip ? 1 : 2 },
            (Expression<Func<Row, bool>>)(x => x.Age > 0));

        connector.Select(typeof(Row), (Expression<Func<Row, bool>>)(x => x.Age > 0)).Cast<Row>()
            .OrderBy(r => r.Age).Select(r => r.Premium).Should().Equal(1, 2, 2, 1, 2);
    }

    [Fact]
    public void LIKE_metacharacters_in_a_search_term_are_literal()
    {
        if (!RequireServer()) return;
        Agrees(x => x.Name != null && x.Name.StartsWith("A_1"));
        Agrees(x => x.Name != null && x.Name.Contains("0%"));
        Agrees(x => x.Name != null && x.Name.Contains("[x]"));
        Agrees(x => x.Name != null && x.Name.EndsWith("]"));
    }

    [Fact]
    public void A_value_on_the_left_of_a_comparison_is_flipped()
    {
        if (!RequireServer()) return;
        Agrees(x => 25 > x.Age);
        Agrees(x => 25 <= x.Age);
    }
}
