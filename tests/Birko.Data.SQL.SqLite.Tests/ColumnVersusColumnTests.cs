using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-574. A comparison between two columns of the same row parsed both operands into one condition, the right
/// overwriting the left with no value bound: <c>x.A == x.B</c> rendered <c>B = NULL</c> and <c>x.A.Date == x.B.Date</c>
/// rendered <c>DATE(B) = NULL</c>. Zero rows, no error. Measured against a real database, never against rendered SQL
/// alone, because the defect is a wrong answer.
/// </summary>
public class ColumnVersusColumnTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-colcol-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("ColCol")]
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

    private Data.SQL.Connectors.SqLiteConnector Seeded()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "cc.db" });
        var connector = (Data.SQL.Connectors.SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });
        // same-day: same date, different times. later: Closed a day after Opened. earlier: Closed a day before Opened.
        // NA/NB: equal values, different values, both null, one null.
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "same-day", A = 1, B = 1, NA = 1, NB = 1, Opened = Day.AddHours(9), Closed = Day.AddHours(17) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "later", A = 1, B = 2, NA = 1, NB = 2, Opened = Day.AddHours(9), Closed = Day.AddDays(1).AddHours(9) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "earlier", A = 3, B = 2, NA = null, NB = null, Opened = Day.AddHours(9), Closed = Day.AddDays(-1).AddHours(9) });
        connector.Insert(new Row { Guid = Guid.NewGuid(), Name = "half", A = 5, B = 5, NA = 4, NB = null, Opened = Day, Closed = Day });
        return connector;
    }

    private string[] Names(Expression<Func<Row, bool>> filter)
        => Seeded().Select(typeof(Row), filter).Cast<Row>().Select(r => r.Name!).OrderBy(n => n).ToArray();

    [Fact]
    public void Plain_column_equal_to_column() => Names(x => x.A == x.B).Should().Equal("half", "same-day");

    [Fact]
    public void Plain_column_not_equal_to_column() => Names(x => x.A != x.B).Should().Equal("earlier", "later");

    [Fact]
    public void Plain_column_less_than_column() => Names(x => x.A < x.B).Should().Equal("later");

    [Fact]
    public void Plain_column_comparison_inside_a_conjunction() => Names(x => x.A <= x.B && x.Name != "half").Should().Equal("later", "same-day");

    [Fact]
    public void Datetime_columns_compare_raw() => Names(x => x.Opened < x.Closed).Should().Equal("later", "same-day");

    [Fact]
    public void Nullable_columns_are_equal_as_CSharp_says_both_nulls_included()
        => Names(x => x.NA == x.NB).Should().Equal("earlier", "same-day");

    [Fact]
    public void Nullable_columns_differ_as_CSharp_says_one_null_included()
        => Names(x => x.NA != x.NB).Should().Equal("half", "later");

    [Fact]
    public void Nullable_columns_ordered_never_match_a_null()
        => Names(x => x.NA < x.NB).Should().Equal("later");

    [Theory]
    [InlineData("==")]
    [InlineData("!=")]
    [InlineData("<")]
    [InlineData(">")]
    public void The_date_of_one_column_against_another_is_refused_not_answered_empty(string op)
    {
        Expression<Func<Row, bool>> filter = op switch
        {
            "==" => x => x.Opened.Date == x.Closed.Date,
            "!=" => x => x.Opened.Date != x.Closed.Date,
            "<" => x => x.Opened.Date < x.Closed.Date,
            _ => x => x.Opened.Date > x.Closed.Date,
        };
        var act = () => Names(filter);
        act.Should().Throw<NotSupportedException>().WithMessage("*.Date*");
    }

    [Fact]
    public void The_date_of_a_column_against_a_value_still_works()
        => Names(x => x.Closed.Date == Day).Should().Equal("half", "same-day");
}
