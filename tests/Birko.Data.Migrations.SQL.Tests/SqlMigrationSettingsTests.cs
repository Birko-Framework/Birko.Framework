using Birko.Configuration;
using Birko.Data.Migrations.SQL.Settings;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

public class SqlMigrationSettingsTests
{
    [Fact]
    public void LoadFrom_RemoteSettings_CopiesTheWholeChain()
    {
        // CR-L151: the RemoteSettings store ctor now copies via LoadFrom rather than hand-listing fields,
        // so the whole inherited chain (incl. UseSecure) is carried, not just the enumerated properties.
        var remote = new RemoteSettings
        {
            Location = "db.example.com",
            Port = 1433,
            Name = "app",
            UserName = "sa",
            Password = "secret",
            UseSecure = true
        };

        var settings = new SqlMigrationSettings();
        settings.LoadFrom(remote);

        settings.Location.Should().Be("db.example.com");
        settings.Port.Should().Be(1433);
        settings.Name.Should().Be("app");
        settings.UserName.Should().Be("sa");
        settings.Password.Should().Be("secret");
        settings.UseSecure.Should().BeTrue("the manual field-copy ctor used to drop UseSecure");
    }

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
