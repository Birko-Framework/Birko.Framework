using System;
using Birko.Data.MongoDB.Serialization;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-498 — a <see cref="PropertyUpdate{T}"/> renders to one update document carrying <c>$set</c> and
/// <c>$inc</c>, and an increment on a field the driver stores as a string is refused before anything is sent
/// (<c>$inc</c> would fail at the server). The driver's default <c>decimal</c> representation is Decimal128, so only an
/// explicit string representation reaches the refusal.
/// </summary>
public class MongoPropertyUpdateTranslatorTests
{
    /// <summary>
    /// Rule 61: rendering and the refusal both look up serializers, which freezes an automapped class map; without
    /// the framework registration first, a later MongoSerializationTests run in the same process fails.
    /// </summary>
    public MongoPropertyUpdateTranslatorTests() => MongoSerialization.EnsureRegistered();

    public class Doc : Data.Models.AbstractModel
    {
        public string? Name { get; set; }
        public int Hits { get; set; }
        public double Ratio { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Price { get; set; }

        [BsonRepresentation(BsonType.String)]
        public decimal PriceAsText { get; set; }
    }

    private static BsonDocument Render(UpdateDefinition<Doc> definition)
        => definition.Render(new RenderArgs<Doc>(BsonSerializer.LookupSerializer<Doc>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    [Fact]
    public void Set_And_Increment_Render_As_Set_And_Inc_In_One_Document()
    {
        var rendered = Render(MongoPropertyUpdateTranslator.Build(
            new PropertyUpdate<Doc>().Set(x => x.Name, "a").Increment(x => x.Hits, 1).Decrement(x => x.Ratio, 0.5)));

        rendered["$set"].AsBsonDocument["Name"].AsString.Should().Be("a");
        rendered["$inc"].AsBsonDocument["Hits"].AsInt32.Should().Be(1);
        rendered["$inc"].AsBsonDocument["Ratio"].AsDouble.Should().Be(-0.5);
    }

    [Fact]
    public void A_Decimal128_Decimal_Increments_As_Decimal128()
    {
        var rendered = Render(MongoPropertyUpdateTranslator.Build(new PropertyUpdate<Doc>().Increment(x => x.Price, 0.2m)));

        rendered["$inc"].AsBsonDocument["Price"].AsDecimal.Should().Be(0.2m);
    }

    [Fact]
    public void An_Increment_On_A_String_Stored_Field_Is_Refused_Naming_The_Fix()
    {
        var act = () => MongoPropertyUpdateTranslator.Build(new PropertyUpdate<Doc>().Increment(x => x.PriceAsText, 0.2m));

        act.Should().Throw<NotSupportedException>().WithMessage("*PriceAsText*Decimal128*");
    }

    [Fact]
    public void A_Set_On_A_String_Stored_Field_Is_Still_Allowed()
    {
        var act = () => MongoPropertyUpdateTranslator.Build(new PropertyUpdate<Doc>().Set(x => x.PriceAsText, 0.2m));

        act.Should().NotThrow();
    }
}
