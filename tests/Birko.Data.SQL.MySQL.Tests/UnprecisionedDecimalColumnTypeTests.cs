using System;
using System.Data;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.Exceptions;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.MySQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MySQL.Tests;

/// <summary>
/// TASK-512 — the column type of a <c>decimal</c> whose model leaves precision or scale undeclared, offline.
/// <para>
/// A bare <c>DECIMAL</c> is <c>decimal(10,0)</c> on MySQL 8.4: scale 0, so <c>7.5</c> was stored as <c>8</c>
/// with no error (measured). The missing half now takes the canonical default
/// (<see cref="AbstractConnectorBase.DefaultDecimalPrecision"/>, <see cref="AbstractConnectorBase.DefaultDecimalScale"/>);
/// a declared half is honoured — before this a lone <c>[PrecisionField]</c> was silently ignored. The round trip
/// against a server is in <c>SchemaDriftLiveTests</c>.
/// </para>
/// </summary>
public class UnprecisionedDecimalColumnTypeTests
{
    [Table("T512Bare")]
    public class Bare { [PrimaryField] public Guid? Guid { get; set; } public decimal Amount { get; set; } public decimal? Maybe { get; set; } }

    [Table("T512Declared")]
    public class Declared { [PrimaryField] public Guid? Guid { get; set; } [PrecisionField(18)] [ScaleField(2)] public decimal Amount { get; set; } }

    [Table("T512PrecisionOnly")]
    public class PrecisionOnly { [PrimaryField] public Guid? Guid { get; set; } [PrecisionField(10)] public decimal Amount { get; set; } }

    [Table("T512ScaleOnly")]
    public class ScaleOnly { [PrimaryField] public Guid? Guid { get; set; } [ScaleField(2)] public decimal Amount { get; set; } }

    [Table("T512TooNarrow")]
    public class TooNarrow { [PrimaryField] public Guid? Guid { get; set; } [PrecisionField(4)] public decimal Amount { get; set; } }

    private static MySQLConnector Connector() => new(new MySqlSettings("localhost", "db", "user", "pass"));

    private static string TypeOf(Type model, string property)
    {
        var field = DataBase.LoadTable(model).Fields[property];
        return Connector().ConvertType(field.Type, field);
    }

    [Fact]
    public void An_unprecisioned_decimal_gets_the_canonical_precision_and_scale()
    {
        TypeOf(typeof(Bare), nameof(Bare.Amount)).Should().Be("DECIMAL(22,6)",
            "a bare DECIMAL is decimal(10,0) here, and scale 0 rounds every fraction away");
        TypeOf(typeof(Bare), nameof(Bare.Maybe)).Should().Be("DECIMAL(22,6)");
    }

    [Fact]
    public void A_full_declaration_is_honoured_unchanged()
        => TypeOf(typeof(Declared), nameof(Declared.Amount)).Should().Be("DECIMAL(18,2)");

    [Fact]
    public void A_lone_precision_is_honoured_instead_of_ignored()
        => TypeOf(typeof(PrecisionOnly), nameof(PrecisionOnly.Amount)).Should().Be("DECIMAL(10,6)");

    [Fact]
    public void A_lone_scale_is_honoured_instead_of_ignored()
        => TypeOf(typeof(ScaleOnly), nameof(ScaleOnly.Amount)).Should().Be("DECIMAL(22,2)");

    [Fact]
    public void A_precision_too_narrow_for_the_default_scale_is_refused_by_name()
    {
        var act = () => TypeOf(typeof(TooNarrow), nameof(TooNarrow.Amount));

        act.Should().Throw<FieldAttributeException>().WithMessage("*Amount*precision 4*scale 6*");
    }

    [Fact]
    public void A_decimal_that_is_not_a_DecimalField_gets_the_default_too()
    {
        // The migrations path builds a property-less field of DbType.Decimal; a null field is public surface.
        Connector().ConvertType(DbType.Decimal, null!).Should().Be("DECIMAL(22,6)");
    }
}
