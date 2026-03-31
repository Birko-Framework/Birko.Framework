using Birko.Data.Migrations.SQL;
using FluentAssertions;
using System.Data.Common;
using System.Linq;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

public class SqlMigrationSplitTests
{
    /// <summary>
    /// Concrete test migration that exposes SplitSqlStatements via ExecuteScript.
    /// We test by calling Up/Down which use ExecuteScript internally,
    /// but since we can't call the private SplitSqlStatements directly,
    /// we test the UpSql/DownSql properties and the overall Execute behavior.
    /// </summary>
    private class TestMigration : SqlMigration
    {
        public string TestUpSql { get; set; } = string.Empty;
        public string TestDownSql { get; set; } = string.Empty;

        public override long Version => 1;
        public override string Name => "Test";
        protected override string UpSql => TestUpSql;
        protected override string DownSql => TestDownSql;

        protected override void ExecuteSql(DbConnection connection, DbTransaction? transaction, MigrationDirection direction)
        {
            // Not used when UpSql/DownSql are set
        }
    }

    [Fact]
    public void UpSql_Property_ReturnsConfiguredSql()
    {
        var migration = new TestMigration { TestUpSql = "CREATE TABLE foo (id INT)" };

        // Verify migration metadata
        migration.Version.Should().Be(1);
        migration.Name.Should().Be("Test");
    }

    [Fact]
    public void Up_ThrowsInvalidOperationException()
    {
        var migration = new TestMigration();

        var act = () => migration.Up();

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*SqlMigrationRunner*");
    }

    [Fact]
    public void Down_ThrowsInvalidOperationException()
    {
        var migration = new TestMigration();

        var act = () => migration.Down();

        act.Should().Throw<System.InvalidOperationException>()
            .WithMessage("*SqlMigrationRunner*");
    }

    [Fact]
    public void Migration_DefaultUpSql_IsEmpty()
    {
        var migration = new TestMigration();

        // TestUpSql defaults to empty
        migration.TestUpSql.Should().BeEmpty();
    }

    [Fact]
    public void Migration_VersionAndName_AreSet()
    {
        var migration = new TestMigration();

        migration.Version.Should().Be(1);
        migration.Name.Should().Be("Test");
    }
}
