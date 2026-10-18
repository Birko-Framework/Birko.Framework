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

        public Guid Owner { get; set; }
        public Guid? Claim { get; set; }
        public int? Budget { get; set; }
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

    // TASK-662: BsonValue.Create(Guid) threw under driver 3.x, so every $set of a Guid failed (the job queue's claim).
    [Fact]
    public void TASK_662_a_Guid_and_a_null_are_set_as_the_member_serializes_them()
    {
        var owner = Guid.NewGuid();
        var claim = Guid.NewGuid();
        var rendered = Render(MongoPropertyUpdateTranslator.Build(
            new PropertyUpdate<Doc>().Set(x => x.Owner, owner).Set(x => x.Claim, claim).Set(x => x.Budget, null)));

        var set = rendered["$set"].AsBsonDocument;
        var written = new Doc { Owner = owner, Claim = claim }.ToBsonDocument();
        set["Owner"].Should().Be(written["Owner"], "the same representation the document is written with");
        set["Claim"].Should().Be(written["Claim"]);
        set["Budget"].IsBsonNull.Should().BeTrue();
    }
}
