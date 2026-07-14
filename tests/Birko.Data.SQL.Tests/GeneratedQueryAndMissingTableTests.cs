using System;
using System.Data;
using Birko.Data.SQL;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// CR-L173: DataBase.GetGeneratedQuery must replace longer parameter names first, otherwise a name that
/// is a prefix of another (@WHEREName0_5 vs @WHEREName0_50) corrupts the rendered diagnostic SQL.
/// CR-L176: AbstractConnectorBase.IsMissingTableException is the overridable seam whose base match is
/// SQLite's "no such table" wording.
/// </summary>
public class GeneratedQueryAndMissingTableTests
{
    private static TestDbParameter Param(string name, DbType type, object? value)
        => new() { ParameterName = name, DbType = type, Value = value };

    [Fact]
    public void GetGeneratedQuery_replaces_longer_parameter_names_first()
    {
        var cmd = new TestDbCommand
        {
            CommandText = "SELECT * FROM T WHERE A = @WHEREName0_5 AND B = @WHEREName0_50",
        };
        // Added shorter-name-first on purpose — the render must still resolve each independently.
        cmd.Parameters.Add(Param("@WHEREName0_5", DbType.Int32, 7));
        cmd.Parameters.Add(Param("@WHEREName0_50", DbType.Int32, 42));

        var rendered = global::Birko.Data.SQL.DataBase.GetGeneratedQuery(cmd);

        rendered.Should().Be("SELECT * FROM T WHERE A = 7 AND B = 42");
    }

    [Fact]
    public void GetGeneratedQuery_quotes_string_parameters()
    {
        var cmd = new TestDbCommand { CommandText = "SELECT * FROM T WHERE Name = @p0" };
        cmd.Parameters.Add(Param("@p0", DbType.String, "abc"));

        global::Birko.Data.SQL.DataBase.GetGeneratedQuery(cmd).Should().Be("SELECT * FROM T WHERE Name = 'abc'");
    }

    [Theory]
    [InlineData("SQLite Error 1: 'no such table: Widgets'.", true)]
    [InlineData("NO SUCH TABLE: widgets", true)]
    [InlineData("some unrelated error", false)]
    public void Base_IsMissingTableException_matches_sqlite_wording(string message, bool expected)
    {
        var connector = new FakeConnector();

        connector.IsMissingTableException(new Exception(message)).Should().Be(expected);
    }

    // Minimal concrete connector to reach the base virtual (the base is abstract).
    private sealed class FakeConnector : Birko.Data.SQL.Connectors.AbstractConnectorBase
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }
}
