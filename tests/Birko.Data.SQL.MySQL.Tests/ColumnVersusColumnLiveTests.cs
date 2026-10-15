using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using MySqlConnector;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-574 on MySQL. A comparison between two columns of the same row rendered <c>B = NULL</c> (the right operand
/// overwrote the left, no value bound): zero rows, no error. Each operand is now rendered; <c>==</c>/<c>!=</c> between
/// nullable columns are null-safe; a <c>.Date</c> of one column against another is refused.
/// </summary>
/// <remarks>Gated on the provider's host variable; set <c>BIRKO_REQUIRE_LIVE</c> so a missing server fails.</remarks>
public class ColumnVersusColumnLiveTests
{
    private static string? Host => Environment.GetEnvironmentVariable("BIRKO_MYSQL_HOST");
    private static int Port => int.TryParse(Environment.GetEnvironmentVariable("BIRKO_MYSQL_PORT"), out var p) ? p : 3306;
    private static string User => Environment.GetEnvironmentVariable("BIRKO_MYSQL_USER") ?? "root";
    private static string Password => Environment.GetEnvironmentVariable("BIRKO_MYSQL_PASSWORD") ?? "root";
    private static string Database => Environment.GetEnvironmentVariable("BIRKO_MYSQL_DB") ?? "birkoview";
    private static bool RequireLive => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE"));

    private readonly ITestOutputHelper _output;

    public ColumnVersusColumnLiveTests(ITestOutputHelper output) => _output = output;

    private bool RequireServer()
    {
        if (!string.IsNullOrWhiteSpace(Host)) return true;
        const string message = "SKIPPED: no live MySQL. Set BIRKO_MYSQL_HOST to exercise this test; "
                             + "set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        _output.WriteLine(message);
        if (RequireLive) throw new InvalidOperationException(message);
        return false;
    }

    private static MySqlSettings Settings() => new(Host!, Database, User, Password, Port);

    private static void Exec(string sql)
    {
        using var connection = new MySqlConnection(Settings().GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Table("MyColCol")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int A { get; set; }
        public int B { get; set; }
        public int? NA { get; set; }
        public int? NB { get; set; }
        public DateTime Opened { get; set; }
        public DateTime Closed { get; set; }
    }

    private static readonly DateTime Day = new(2026, 10, 15, 0, 0, 0);

    private string[] Names(Expression<Func<Row, bool>> filter)
    {
        Exec("DROP TABLE IF EXISTS `MyColCol`");
        var connector = new MySQLConnector(Settings());
        connector.CreateTable(new[] { typeof(Row) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "same-day", A = 1, B = 1, NA = 1, NB = 1, Opened = Day.AddHours(9), Closed = Day.AddHours(17) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "later", A = 1, B = 2, NA = 1, NB = 2, Opened = Day.AddHours(9), Closed = Day.AddDays(1).AddHours(9) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "earlier", A = 3, B = 2, NA = null, NB = null, Opened = Day.AddHours(9), Closed = Day.AddDays(-1).AddHours(9) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "half", A = 5, B = 5, NA = 4, NB = null, Opened = Day, Closed = Day });
        return connector.Select(typeof(Row), filter).Cast<Row>().Select(r => r.Name!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void Two_columns_compare_by_their_values()
    {
        if (!RequireServer()) return;
        Names(x => x.A == x.B).Should().Equal("half", "same-day");
        Names(x => x.A != x.B).Should().Equal("earlier", "later");
        Names(x => x.A < x.B).Should().Equal("later");
        Names(x => x.Opened < x.Closed).Should().Equal("later", "same-day");
    }

    [Fact]
    public void Nullable_columns_compare_as_CSharp_does()
    {
        if (!RequireServer()) return;
        Names(x => x.NA == x.NB).Should().Equal("earlier", "same-day");
        Names(x => x.NA != x.NB).Should().Equal("half", "later");
        Names(x => x.NA < x.NB).Should().Equal("later");
    }

    [Fact]
    public void The_date_of_one_column_against_another_is_refused()
    {
        if (!RequireServer()) return;
        var act = () => Names(x => x.Opened.Date == x.Closed.Date);
        act.Should().Throw<NotSupportedException>().WithMessage("*.Date*");
    }
}
