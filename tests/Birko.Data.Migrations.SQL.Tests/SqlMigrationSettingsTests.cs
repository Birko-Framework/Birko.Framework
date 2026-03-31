using Birko.Data.Migrations.SQL.Settings;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

public class SqlMigrationSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new SqlMigrationSettings();

        settings.MigrationsTable.Should().Be("__Migrations");
        settings.Schema.Should().BeNull();
        settings.UseTransaction.Should().BeTrue();
        settings.TransactionTimeout.Should().Be(30);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var settings = new SqlMigrationSettings
        {
            MigrationsTable = "CustomMigrations",
            Schema = "dbo",
            UseTransaction = false,
            TransactionTimeout = 60
        };

        settings.MigrationsTable.Should().Be("CustomMigrations");
        settings.Schema.Should().Be("dbo");
        settings.UseTransaction.Should().BeFalse();
        settings.TransactionTimeout.Should().Be(60);
    }
}
