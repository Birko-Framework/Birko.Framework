using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// SH-H037 — the DDL half. <c>long</c> / <c>short</c> / <c>double</c> / <c>float</c> / <c>byte[]</c>
/// properties produced no field at all, so these <c>ConvertType</c> arms — which already existed — were
/// unreachable from an attribute-driven model. No live MySQL required; <c>ConvertType</c> /
/// <c>FieldDefinition</c> are pure.
/// <para>
/// Each case goes through <c>DataBase.LoadTable</c> rather than constructing the field class by hand —
/// a hand-built field survives a dispatch-only revert, so such a test cannot witness this fix.
/// </para>
/// </summary>
public class MySQLPrimitiveColumnTypeTests
{
    [Table("MyPrimitiveSpread")]
    public class Sample : AbstractLogModel
    {
        public long Ticks { get; set; }
        public short Small { get; set; }
        public double Ratio { get; set; }
        public float Single { get; set; }
        public byte[]? Blob { get; set; }
    }

    private static MySQLConnector NewConnector()
        => new(new MySqlSettings("localhost", "db", "user", "pass"));

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
    public void Double_DeclaresDouble_NotFloat()
    {
        // MySQL's FLOAT is the 4-byte type; DOUBLE is the 8-byte one a C# double needs. Asserting the
        // absence of FLOAT is what makes this more than a restatement of the mapping.
        var definition = DefinitionFor(nameof(Sample.Ratio));

        definition.Should().Contain("DOUBLE");
        definition.Should().NotContain("FLOAT");
    }

    [Fact]
    public void Float_DeclaresFloat_NotTinyint()
    {
        var definition = DefinitionFor(nameof(Sample.Single));

        definition.Should().Contain("FLOAT");
        definition.Should().NotContain("TINYINT");
    }

    [Fact]
    public void ByteArray_DeclaresLongblob_AndIsNullableByDefault()
    {
        var definition = DefinitionFor(nameof(Sample.Blob));

        definition.Should().Contain("LONGBLOB");
        definition.Should().NotContain("NOT NULL");
    }
}
