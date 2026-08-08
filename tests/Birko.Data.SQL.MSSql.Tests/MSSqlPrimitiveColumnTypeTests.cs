using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// SH-H037 — the DDL half. <c>long</c> / <c>short</c> / <c>double</c> / <c>float</c> / <c>byte[]</c>
/// properties produced no field at all, so these <c>ConvertType</c> arms — which already existed — were
/// unreachable from an attribute-driven model. No live SQL Server required; <c>ConvertType</c> /
/// <c>FieldDefinition</c> are pure.
/// <para>
/// Each case goes through <c>DataBase.LoadTable</c> rather than constructing the field class by hand —
/// a hand-built field survives a dispatch-only revert, so such a test cannot witness this fix.
/// </para>
/// </summary>
public class MSSqlPrimitiveColumnTypeTests
{
    [Table("MsPrimitiveSpread")]
    public class Sample : AbstractLogModel
    {
        public long Ticks { get; set; }
        public short Small { get; set; }
        public double Ratio { get; set; }
        public float Single { get; set; }
        public byte[]? Blob { get; set; }
    }

    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    private static string DefinitionFor(string property)
    {
        var table = Birko.Data.SQL.DataBase.LoadTable(typeof(Sample));
        var field = table.Fields.Values.FirstOrDefault(f => f.Property?.Name == property);
        field.Should().NotBeNull($"'{property}' must map to a column at all — SH-H037 was that it did not");
        return NewConnector().FieldDefinition(field!);
    }

    [Fact]
    public void Long_DeclaresBigint()
        => DefinitionFor(nameof(Sample.Ticks)).Should().Contain("BIGINT").And.Contain("NOT NULL");

    [Fact]
    public void Short_DeclaresSmallint()
        => DefinitionFor(nameof(Sample.Small)).Should().Contain("SMALLINT");

    [Fact]
    public void Double_DeclaresFloat()
        // SQL Server's FLOAT is the 8-byte type and REAL the 4-byte one — the opposite naming to MySQL.
        => DefinitionFor(nameof(Sample.Ratio)).Should().Contain("FLOAT");

    [Fact]
    public void Float_DeclaresReal_NotTinyint()
    {
        // CR-H087: a float grouped with SByte/Byte generated TINYINT (0-255), dropping negatives and
        // fractions outright.
        var definition = DefinitionFor(nameof(Sample.Single));

        definition.Should().Contain("REAL");
        definition.Should().NotContain("TINYINT");
    }

    [Fact]
    public void ByteArray_DeclaresVarbinaryMax_AndIsNullableByDefault()
    {
        // CR-M137: a bare BINARY defaults to BINARY(1) in SQL Server, truncating any blob to one byte.
        var definition = DefinitionFor(nameof(Sample.Blob));

        definition.Should().Contain("VARBINARY(MAX)");
        definition.Should().NotContain("NOT NULL");
    }
}
