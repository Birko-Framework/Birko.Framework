using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.Tests;

/// <summary>
/// SH-H037 — the DDL half. <c>long</c> / <c>short</c> / <c>double</c> / <c>float</c> / <c>byte[]</c>
/// properties produced no field at all, so these <c>ConvertType</c> arms — which already existed — were
/// unreachable from an attribute-driven model. No live PostgreSQL required; <c>ConvertType</c> /
/// <c>FieldDefinition</c> are pure.
/// <para>
/// Each case goes through <c>DataBase.LoadTable</c> rather than constructing the field class by hand.
/// That is deliberate and was learned the hard way: a first version built <c>new LongField(...)</c>
/// directly and passed with the fix reverted, because the field classes survive a dispatch-only revert.
/// Driving the model type through the real mapping puts the defect back in the loop.
/// </para>
/// </summary>
public class PostgreSQLPrimitiveColumnTypeTests
{
    [Table("PgPrimitiveSpread")]
    public class Sample : AbstractLogModel
    {
        public long Ticks { get; set; }
        public short Small { get; set; }
        public double Ratio { get; set; }
        public float Single { get; set; }
        public byte[]? Blob { get; set; }
    }

    private static PostgreSQLConnector NewConnector()
        => new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    /// <summary>The column definition the CREATE TABLE would carry for <paramref name="property"/>.</summary>
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
    public void Double_DeclaresDoublePrecision()
        => DefinitionFor(nameof(Sample.Ratio)).Should().Contain("DOUBLE PRECISION");

    [Fact]
    public void Float_DeclaresReal_NotSmallint()
    {
        // CR-H087: a float grouped with SByte/Byte produced SMALLINT, truncating fractions.
        var definition = DefinitionFor(nameof(Sample.Single));

        definition.Should().Contain("REAL");
        definition.Should().NotContain("SMALLINT");
    }

    [Fact]
    public void ByteArray_DeclaresBytea_AndIsNullableByDefault()
    {
        var definition = DefinitionFor(nameof(Sample.Blob));

        definition.Should().Contain("BYTEA");
        definition.Should().NotContain("NOT NULL");
    }
}
