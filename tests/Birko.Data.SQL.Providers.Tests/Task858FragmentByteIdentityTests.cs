using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Providers.Tests;

/// <summary>
/// TASK-858 — on PostgreSQL, MySQL and SQL Server a value fragment's provider re-render (TASK-856) is byte for byte the
/// portable form the parser wrote into the condition. Only a <c>Math.Round</c> (TASK-857) and MySQL's integer division
/// and <c>double</c> cast (TASK-860) differ, by design, and MySQL's integer widening (TASK-890: MySQL computes every integer in
/// <c>BIGINT</c>, and its <c>CAST</c> takes no <c>BIGINT</c>, so it renders none). Before this, one shape pinned it
/// (<c>ThePortableFragment_OtherProvidersRun_IsUnchanged</c>, SQLite tests); this covers <c>CASE</c>, <c>COALESCE</c>, a
/// conversion, column against column and the <c>IS NULL</c> legs TASK-859 adds, through each connector's own dialect.
/// Offline: the connectors are built, never connected.
/// </summary>
public class Task858FragmentByteIdentityTests
{
    [Table("T858Rows")]
    public class Row : AbstractModel
    {
        public decimal Amount { get; set; }
        public decimal? Optional { get; set; }
        public int Hits { get; set; }
        public bool Vip { get; set; }
    }

    private sealed class PostgreSql : PostgreSQLConnector
    {
        public PostgreSql() : base(new PostgreSqlSettings("t858.example.net", "db", "user", "pw")) { }
        public SqlValueFragmentDialect Dialect => FragmentDialect;
    }

    private sealed class MySql : MySQLConnector
    {
        public MySql() : base(new MySqlSettings("t858.example.net", "db", "user", "pw")) { }
        public SqlValueFragmentDialect Dialect => FragmentDialect;
    }

    private sealed class SqlServer : MSSqlConnector
    {
        public SqlServer() : base(new MSSqlSettings("t858.example.net", "db", "user", "pw", 1433, true)) { }
        public SqlValueFragmentDialect Dialect => FragmentDialect;
    }

    private static readonly decimal[] Ids = { 1m, 2m };

    public static readonly IReadOnlyDictionary<string, Expression<Func<Row, bool>>> Shapes =
        new Dictionary<string, Expression<Func<Row, bool>>>
        {
            ["CASE < 8"] = x => (x.Amount > 6m ? x.Amount : 0m) < 8m,
            ["CASE on a bool column == 1"] = x => (x.Vip ? x.Amount : x.Hits) == 1m,
            ["COALESCE < 8"] = x => (x.Optional ?? 0m) < 8m,
            ["COALESCE != 8 (nullable leg)"] = x => (x.Optional ?? x.Amount) + x.Optional != 8m,
            ["(decimal)Hits + Amount == 10"] = x => (decimal)x.Hits + x.Amount == 10m,
            ["(long)Hits + 1 == 10"] = x => (long)x.Hits + 1L == 10L,
            ["Amount < Optional"] = x => x.Amount < x.Optional,
            ["Amount == Optional (null-safe)"] = x => x.Amount == x.Optional,
            ["Amount + 1 != Optional - 1"] = x => x.Amount + 1m != x.Optional - 1m,
            ["Ids.Contains(Amount * 2)"] = x => Ids.Contains(x.Amount * 2m),
        };

    /// <summary>The by-design differences: TASK-890's integer widening is a cast on PostgreSQL and SQL Server, nothing on MySQL.</summary>
    private static readonly IReadOnlyDictionary<(string Provider, string Portable), string> Designed =
        new Dictionary<(string, string), string>
        {
            [("MySQL", "(CAST(T858Rows.Hits AS BIGINT) + 1)")] = "(T858Rows.Hits + 1)",
        };

    public static IEnumerable<object[]> Cases()
        => from provider in new[] { "PostgreSQL", "MySQL", "SQL Server" }
           from shape in Shapes.Keys
           select new object[] { provider, shape };

    private static SqlValueFragmentDialect DialectOf(string provider) => provider switch
    {
        "PostgreSQL" => new PostgreSql().Dialect,
        "MySQL" => new MySql().Dialect,
        _ => new SqlServer().Dialect,
    };

    private static IEnumerable<Condition> Flatten(IEnumerable<Condition> conditions)
        => conditions.SelectMany(c => new[] { c }.Concat(Flatten(c.SubConditions ?? Enumerable.Empty<Condition>())));

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_provider_renders_the_fragment_byte_for_byte_as_the_portable_form(string provider, string shape)
    {
        var dialect = DialectOf(provider);
        dialect.Should().NotBeSameAs(SqlValueFragmentDialect.Portable, "every connector re-renders through its own dialect since TASK-857");

        var rendered = Flatten(DataBase.ParseConditionExpression(Shapes[shape])).Where(c => c.NameFragment != null).ToList();

        rendered.Should().NotBeEmpty($"'{shape}' is a value fragment");
        foreach (var condition in rendered)
        {
            var expected = Designed.TryGetValue((provider, condition.Name!), out var designed) ? designed : condition.Name;
            condition.NameFragment!(dialect).Should().Be(expected, $"{provider} runs the portable '{condition.Name}'");
            if (condition.FieldFragment != null)
                condition.FieldFragment(dialect).Should().Be((string)condition.Values!.Cast<object>().Single(),
                    $"{provider} runs the portable right side");
        }
    }
}
