using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// CR-M142: auto-increment field definitions used to be produced by String.Replace on the composed
/// definition (brittle string surgery). FieldDefinition now emits the SERIAL pseudo-type directly at
/// the point ConvertType would run — no post-hoc string mutation.
/// </summary>
public class PostgreSQLSerialTests
{
    private sealed class Sample
    {
        public int Id { get; set; }
    }

    private static PostgreSQLConnector NewConnector()
        => new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    private static IntegerField IdField(bool autoincrement)
        => new(typeof(Sample).GetProperty(nameof(Sample.Id))!, "Id", autoincrement: autoincrement);

    [Fact]
    public void Autoincrement_int_emits_SERIAL()
    {
        var def = NewConnector().FieldDefinition(IdField(autoincrement: true));

        def.Should().Contain("SERIAL");
        def.Should().NotContain("INTEGER");
        def.Should().StartWith("Id ");
    }

    [Fact]
    public void Non_autoincrement_int_stays_INTEGER()
    {
        var def = NewConnector().FieldDefinition(IdField(autoincrement: false));

        def.Should().Contain("INTEGER");
        def.Should().NotContain("SERIAL");
    }
}
