using System;
using System.Data;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// CR-M137: DbType.Object/Binary mapped to bare "BINARY", which SQL Server defaults to BINARY(1) —
/// truncating any blob/serialized object to a single byte. They now map to VARBINARY(MAX).
/// </summary>
public class MSSqlBinaryTypeTests
{
    private sealed class Sample
    {
        public byte[]? Data { get; set; }
    }

    private static MSSqlConnector NewConnector() => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    // ConvertType's Binary/Object branch returns a fixed type independent of the field's length,
    // so any field instance suffices to exercise it.
    private static AbstractField AnyField()
        => new DateTimeField(typeof(Sample).GetProperty(nameof(Sample.Data))!, "Data");

    [Theory]
    [InlineData(DbType.Binary)]
    [InlineData(DbType.Object)]
    public void Binary_and_Object_map_to_VARBINARY_MAX(DbType type)
    {
        NewConnector().ConvertType(type, AnyField()).Should().Be("VARBINARY(MAX)");
    }
}
