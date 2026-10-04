using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-498 — the SET fragments a <see cref="PropertyUpdate{T}"/> renders to. Columns are bare (rule 17: framework
/// DDL creates them unquoted, so a quoted mixed-case name would miss the folded column on PostgreSQL); the
/// connector quotes the table. The values the fragments produce are asserted end-to-end in
/// <c>Birko.Data.SQL.SqLite.Tests.PropertyUpdateIncrementEndToEndTests</c>.
/// </summary>
public class PropertyUpdateSqlTranslatorTests
{
    [Table("TranslatorRows")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public decimal Price { get; set; }
    }

    /// <summary>A connector that keeps the base <see cref="Birko.Data.SQL.Connectors.AbstractConnectorBase.IncrementExpression"/>.</summary>
    private class BaseConnector : Birko.Data.SQL.Connectors.AbstractConnectorBase
    {
        public BaseConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    /// <summary>TASK-513 — a provider that adds a column type through a function of its own.</summary>
    private sealed class FunctionAddConnector : BaseConnector
    {
        public override string IncrementExpression(Birko.Data.SQL.Fields.AbstractField field, string column, string parameter)
            => field.Type == System.Data.DbType.Decimal ? $"exact_add({column}, {parameter})" : base.IncrementExpression(field, column, parameter);
    }

    private static readonly BaseConnector Base = new();

    [Fact]
    public void The_Increment_Right_Hand_Side_Is_The_Connectors()
    {
        var (fields, values) = PropertyUpdateSqlTranslator.Translate(
            new PropertyUpdate<Row>().Increment(x => x.Price, 0.2m).Increment(x => x.Hits, 1), new FunctionAddConnector());

        fields[0].Should().Be("Price = exact_add(Price, @SETPrice)");
        fields[1].Should().Be("Hits = Hits + @SETHits", "the override declines the other column types");
        values["@SETPrice"].Should().Be(0.2m);
    }

    [Fact]
    public void Set_And_Increment_Render_Into_One_Field_Map()
    {
        var (fields, values) = PropertyUpdateSqlTranslator.Translate(
            new PropertyUpdate<Row>().Set(x => x.Name, "a").Increment(x => x.Hits, 1), Base);

        fields.Should().HaveCount(2);
        fields[0].Should().Be("Name = @SETName");
        fields[1].Should().Be("Hits = Hits + @SETHits");
        values.Should().Contain("@SETName", "a").And.Contain("@SETHits", 1);
    }

    [Fact]
    public void Decrement_Renders_As_An_Addition_Of_The_Negative_Delta()
    {
        var (fields, values) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Decrement(x => x.Price, 0.5m), Base);

        fields[0].Should().Be("Price = Price + @SETPrice");
        values["@SETPrice"].Should().Be(-0.5m);
    }

    [Fact]
    public void Columns_Are_Not_Quoted()
    {
        var (fields, _) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Increment(x => x.Hits, 1), Base);

        fields[0].Should().NotContain("\"").And.NotContain("[").And.NotContain("`");
    }

    [Fact]
    public void A_Null_Set_Binds_DBNull()
    {
        var (_, values) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Set(x => x.Name, null), Base);

        values["@SETName"].Should().Be(DBNull.Value);
    }
}
