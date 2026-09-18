using System;
using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// CR-H086: MSSqlConnector.ConvertType mapped DbType.Time, DbType.Date AND DbType.DateTime all to
/// SQL "DATE", silently truncating the time component of every DateTime column. These offline tests
/// pin the corrected mappings (no live SQL Server needed — ConvertType is a pure type mapper).
/// </summary>
public class MSSqlConnectorConvertTypeTests
{
    private sealed class Sample
    {
        public DateTime When { get; set; }
    }

    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    private static DateTimeField DateTimeFieldForWhen()
        => new(typeof(Sample).GetProperty(nameof(Sample.When))!, "When");

    [Fact]
    public void DateTime_MapsTo_DateTime2_NotDate()
    {
        var connector = NewConnector();
        var field = DateTimeFieldForWhen();

        connector.ConvertType(DbType.DateTime, field).Should().Be("DATETIME2");
    }

    [Fact]
    public void DateTime2_MapsTo_DateTime2()
    {
        var connector = NewConnector();
        connector.ConvertType(DbType.DateTime2, DateTimeFieldForWhen()).Should().Be("DATETIME2");
    }

    [Fact]
    public void Date_MapsTo_Date()
    {
        var connector = NewConnector();
        connector.ConvertType(DbType.Date, DateTimeFieldForWhen()).Should().Be("DATE");
    }

    [Fact]
    public void Time_MapsTo_Time()
    {
        var connector = NewConnector();
        connector.ConvertType(DbType.Time, DateTimeFieldForWhen()).Should().Be("TIME");
    }

    // CR-H087: DbType.Single (C# float) was grouped with SByte/Byte and mapped to TINYINT,
    // truncating the value. It must map to REAL.
    [Fact]
    public void Single_MapsTo_Real_NotTinyInt()
    {
        var connector = NewConnector();
        connector.ConvertType(DbType.Single, DateTimeFieldForWhen()).Should().Be("REAL");
    }

    [Theory]
    [InlineData(DbType.Boolean, "BIT")]
    [InlineData(DbType.Int32, "INT")]
    [InlineData(DbType.Int64, "BIGINT")]
    [InlineData(DbType.Int16, "SMALLINT")]
    [InlineData(DbType.Byte, "TINYINT")]
    [InlineData(DbType.Double, "FLOAT")]
    [InlineData(DbType.Guid, "UNIQUEIDENTIFIER")]
    public void ConvertType_MapsScalarTypes(DbType type, string expected)
    {
        var connector = NewConnector();
        connector.ConvertType(type, DateTimeFieldForWhen()).Should().Be(expected);
    }
}
