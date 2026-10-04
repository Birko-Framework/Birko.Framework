using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-513 — <b>every connection the SQLite connector hands out carries the <see cref="SqLiteDecimal"/>
/// registrations</b>, and what they compute is exact.
///
/// <para>A <c>decimal</c> column declared <c>TEXT COLLATE BIRKO_DECIMAL</c> cannot be ordered, nor written
/// once indexed, through a connection that lacks the collation (measured: <c>no such collation sequence</c>).
/// So the guard is the connection funnel, both branches of it: <see cref="SqLiteSettings"/> and a plain
/// <see cref="PasswordSettings"/>.</para>
/// </summary>
public class SqLiteDecimalConnectionTests : IDisposable
{
    private readonly string _root;
    private static int _seq;

    public SqLiteDecimalConnectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-decimal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string NextName() => $"decimal{Interlocked.Increment(ref _seq)}.db";

    public static IEnumerable<object[]> Branches() => new[] { new object[] { "SqLiteSettings" }, new object[] { "PasswordSettings" } };

    private DbConnection OpenFromConnector(string branch)
    {
        PasswordSettings settings = branch == "SqLiteSettings"
            ? new SqLiteSettings(_root, NextName())
            : new PasswordSettings(_root, NextName());
        var connection = new SqLiteConnector(settings).CreateConnection(settings);
        connection.Open();
        return connection;
    }

    private static void Exec(DbConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Command(c, sql, ps);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(DbConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Command(c, sql, ps);
        return cmd.ExecuteScalar();
    }

    private static List<decimal> Decimals(DbConnection c, string sql, params (string Name, object? Value)[] ps)
    {
        using var cmd = Command(c, sql, ps);
        using var r = cmd.ExecuteReader();
        var result = new List<decimal>();
        while (r.Read()) result.Add(r.GetDecimal(0));
        return result;
    }

    private static DbCommand Command(DbConnection c, string sql, (string Name, object? Value)[] ps)
    {
        var cmd = (SqliteCommand)c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static void CreateDecimalTable(DbConnection c, params decimal[] values)
    {
        Exec(c, $"CREATE TABLE t (id INTEGER, v TEXT COLLATE {SqLiteDecimal.Collation})");
        Exec(c, "CREATE INDEX ix_t_v ON t (v)");
        for (int i = 0; i < values.Length; i++) Exec(c, "INSERT INTO t VALUES (@i, @v)", ("@i", i), ("@v", values[i]));
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void A_connector_connection_orders_and_ranges_a_decimal_column_numerically(string branch)
    {
        using var c = OpenFromConnector(branch);
        CreateDecimalTable(c, 100m, 9m, -5.5m, 10m, 2.5m, 0.2m, 1234567890123456.123456m);

        Decimals(c, "SELECT v FROM t ORDER BY v").Should().Equal(
            -5.5m, 0.2m, 2.5m, 9m, 10m, 100m, 1234567890123456.123456m);
        Decimals(c, "SELECT v FROM t WHERE v > @p ORDER BY v", ("@p", 9.5m)).Should().Equal(
            10m, 100m, 1234567890123456.123456m);
        Decimals(c, "SELECT MAX(v) FROM t").Should().Equal(1234567890123456.123456m);
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void A_connector_connection_matches_a_decimal_regardless_of_trailing_zeros(string branch)
    {
        using var c = OpenFromConnector(branch);
        CreateDecimalTable(c, 10m, 2.5m);

        Scalar(c, "SELECT COUNT(*) FROM t WHERE v = @p", ("@p", 10.00m)).Should().Be(1L);
        Scalar(c, "SELECT COUNT(*) FROM t WHERE v IN (@a, @b)", ("@a", 10.0m), ("@b", 2.50m)).Should().Be(2L);
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void A_connector_connection_survives_close_and_reopen(string branch)
    {
        using var c = OpenFromConnector(branch);
        CreateDecimalTable(c, 10m, 9m);
        c.Close();
        c.Open();

        Decimals(c, "SELECT v FROM t ORDER BY v").Should().Equal(9m, 10m);
        Exec(c, "INSERT INTO t VALUES (2, @v)", ("@v", 1m));
    }

    /// <summary>
    /// Pins the cost the design accepts: without the registration the column still reads, but cannot be
    /// ordered or written once indexed. If this ever passes without throwing, the collation is no longer
    /// load-bearing and the contract in <see cref="SqLiteDecimal"/>'s remarks is out of date.
    /// </summary>
    [Fact]
    public void A_raw_connection_without_the_registration_cannot_order_or_write_the_column()
    {
        var settings = new SqLiteSettings(_root, NextName());
        using (var c = new SqLiteConnector(settings).CreateConnection(settings))
        {
            c.Open();
            CreateDecimalTable(c, 10m, 9m);
        }

        using var raw = new SqliteConnection(settings.GetConnectionString());
        raw.Open();
        Decimals(raw, "SELECT v FROM t ORDER BY id").Should().Equal(10m, 9m);
        FluentActions.Invoking(() => Decimals(raw, "SELECT v FROM t ORDER BY v"))
            .Should().Throw<SqliteException>().WithMessage($"*no such collation sequence: {SqLiteDecimal.Collation}*");
        FluentActions.Invoking(() => Exec(raw, "INSERT INTO t VALUES (9, '1.0')"))
            .Should().Throw<SqliteException>().WithMessage("*no such collation sequence*");

        SqLiteDecimal.Register(raw);
        Decimals(raw, "SELECT v FROM t ORDER BY v").Should().Equal(9m, 10m);
    }

    [Fact]
    public void Add_is_exact_where_SQL_arithmetic_drifts()
    {
        using var c = OpenFromConnector("SqLiteSettings");
        CreateDecimalTable(c, 10.10m, 1234567890123456.123456m);

        Exec(c, $"UPDATE t SET v = v + @d WHERE id = 0", ("@d", 0.20m));
        Decimals(c, "SELECT v FROM t WHERE id = 0").Single().Should().NotBe(10.30m, "the drift this function exists for");

        Exec(c, $"UPDATE t SET v = @v WHERE id = 0", ("@v", 10.10m));
        Exec(c, $"UPDATE t SET v = {SqLiteDecimal.AddFunction}(v, @d)", ("@d", 0.20m));
        Decimals(c, "SELECT v FROM t ORDER BY id").Should().Equal(10.30m, 1234567890123456.323456m);
        Scalar(c, "SELECT typeof(v) FROM t WHERE id = 0").Should().Be("text");
        Scalar(c, "SELECT v FROM t WHERE id = 0").Should().Be("10.3", "stored in the form a decimal parameter binds as");

        Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}(NULL, @d)", ("@d", 1m)).Should().Be(DBNull.Value);
        Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}(5, @d)", ("@d", 0.5m)).Should().Be("5.5", "an INTEGER storage-class operand");
    }

    [Fact]
    public void Add_refuses_an_overflow_and_an_unparseable_operand_rather_than_rounding()
    {
        using var c = OpenFromConnector("SqLiteSettings");

        FluentActions.Invoking(() => Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}(@a, @b)", ("@a", decimal.MaxValue), ("@b", 1m)))
            .Should().Throw<SqliteException>();
        FluentActions.Invoking(() => Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}('abc', @b)", ("@b", 1m)))
            .Should().Throw<SqliteException>();
        FluentActions.Invoking(() => Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}(x'00', @b)", ("@b", 1m)))
            .Should().Throw<SqliteException>();
    }

    [Fact]
    public void Sum_and_avg_are_exact_skip_nulls_and_answer_null_for_no_rows()
    {
        using var c = OpenFromConnector("SqLiteSettings");
        Exec(c, $"CREATE TABLE t (v TEXT COLLATE {SqLiteDecimal.Collation})");
        for (int i = 0; i < 10; i++) Exec(c, "INSERT INTO t VALUES (@v)", ("@v", 0.1m));
        Exec(c, "INSERT INTO t VALUES (NULL)");
        Exec(c, "INSERT INTO t VALUES (@v)", ("@v", 1234567890123456.123456m));

        Decimals(c, $"SELECT {SqLiteDecimal.SumAggregate}(v) FROM t").Should().Equal(1234567890123457.123456m);
        Decimals(c, $"SELECT {SqLiteDecimal.AvgAggregate}(v) FROM t WHERE v < @p", ("@p", 1m)).Should().Equal(0.1m);
        Scalar(c, $"SELECT {SqLiteDecimal.SumAggregate}(v) FROM t WHERE v > @p", ("@p", 1e20m)).Should().Be(DBNull.Value);
        Scalar(c, $"SELECT {SqLiteDecimal.AvgAggregate}(v) FROM t WHERE v IS NULL").Should().Be(DBNull.Value);
    }

    [Fact]
    public void The_collation_puts_unparseable_text_after_every_number_and_keeps_a_total_order()
    {
        SqLiteDecimal.Compare("9.0", "10.0").Should().BeNegative();
        SqLiteDecimal.Compare("10.0", "10").Should().Be(0);
        SqLiteDecimal.Compare("1e3", "999.5").Should().BePositive();
        SqLiteDecimal.Compare("abc", "1e30").Should().BePositive();
        SqLiteDecimal.Compare("1", "abc").Should().BeNegative();
        SqLiteDecimal.Compare("abc", "abd").Should().BeNegative();
        SqLiteDecimal.Compare("1,000", "1000.0").Should().BePositive("a thousands separator is not a number here");
        SqLiteDecimal.Compare("5-", "-5").Should().BePositive("nor is a trailing sign");
        SqLiteDecimal.Compare(" 5", "5").Should().NotBe(0, "nor is padding");
    }

    [Fact]
    public void Nothing_depends_on_the_thread_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sk-SK");
            using var c = OpenFromConnector("SqLiteSettings");
            CreateDecimalTable(c, 10.5m, 9.25m);

            Decimals(c, "SELECT v FROM t ORDER BY v").Should().Equal(9.25m, 10.5m);
            Scalar(c, $"SELECT {SqLiteDecimal.AddFunction}(v, @d) FROM t WHERE id = 0", ("@d", 0.25m)).Should().Be("10.75");
            SqLiteDecimal.Format(1234.5m).Should().Be("1234.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
