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

    [Fact]
    public void Set_And_Increment_Render_Into_One_Field_Map()
    {
        var (fields, values) = PropertyUpdateSqlTranslator.Translate(
            new PropertyUpdate<Row>().Set(x => x.Name, "a").Increment(x => x.Hits, 1));

        fields.Should().HaveCount(2);
        fields[0].Should().Be("Name = @SETName");
        fields[1].Should().Be("Hits = Hits + @SETHits");
        values.Should().Contain("@SETName", "a").And.Contain("@SETHits", 1);
    }

    [Fact]
    public void Decrement_Renders_As_An_Addition_Of_The_Negative_Delta()
    {
        var (fields, values) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Decrement(x => x.Price, 0.5m));

        fields[0].Should().Be("Price = Price + @SETPrice");
        values["@SETPrice"].Should().Be(-0.5m);
    }

    [Fact]
    public void Columns_Are_Not_Quoted()
    {
        var (fields, _) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Increment(x => x.Hits, 1));

        fields[0].Should().NotContain("\"").And.NotContain("[").And.NotContain("`");
    }

    [Fact]
    public void A_Null_Set_Binds_DBNull()
    {
        var (_, values) = PropertyUpdateSqlTranslator.Translate(new PropertyUpdate<Row>().Set(x => x.Name, null));

        values["@SETName"].Should().Be(DBNull.Value);
    }
}
