using System;
using Birko.Configuration;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-653: SQLite's connection string carries the path and password as values. Plain Microsoft.Data.Sqlite has no
/// SQLCipher, so an encrypted open cannot be measured; the composed string is read back through the driver's builder.
/// </summary>
public class Task653ConnectionStringEscapingTests
{
    private const string Path = @"C:\data\a;Mode=Memory;b.db";
    private const string Password = "x;Data Source=other.db;'\"";

    [Fact]
    public void Settings_compose_the_path_and_password_as_values()
    {
        var settings = new SqLiteSettings(@"C:\data", "a;Mode=Memory;b.db") { Password = Password, CommandTimeout = 45 };

        var read = new SqliteConnectionStringBuilder(settings.GetConnectionString());

        read.DataSource.Should().Be(settings.Path);
        read.Password.Should().Be(Password);
        read.Mode.Should().Be(SqliteOpenMode.ReadWriteCreate, "a ';Mode=Memory' inside the path is part of the file name");
        read.DefaultTimeout.Should().Be(45);
    }

    [Fact]
    public void The_connector_fallback_composes_them_as_values_too()
    {
        var remote = new PasswordSettings(@"C:\data", "a;Mode=Memory;b.db") { Password = Password };

        var composed = SqLiteConnector.ComposeConnectionString(System.IO.Path.Combine(@"C:\data", "a;Mode=Memory;b.db"), Password, defaultTimeout: null);
        var read = new SqliteConnectionStringBuilder(composed);

        read.DataSource.Should().Be(System.IO.Path.Combine(remote.Location!, remote.Name!));
        read.Password.Should().Be(Password);
        read.Mode.Should().Be(SqliteOpenMode.ReadWriteCreate);
    }
}
