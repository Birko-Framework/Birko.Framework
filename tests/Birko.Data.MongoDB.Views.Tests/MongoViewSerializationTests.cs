using System;
using Birko.Data.Models;
using Birko.Data.MongoDB.Views;
using Birko.Data.Views;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Xunit;

namespace Birko.Data.MongoDB.Views.Tests;

/// <summary>
/// TASK-219 — a view type is a projection shape, not an entity, and the driver's default conventions
/// assume the opposite. <c>MongoViewStore</c> renders its <c>$match</c> through the view type's class
/// map and deserializes results through the same map, so a map that disagrees with the projection
/// produces a filter that <b>silently matches nothing</b> rather than an error.
///
/// Non-gated on purpose: class-map shape and filter rendering need no server. The live half is in
/// <c>MongoViewIdentityLiveTests</c>.
/// </summary>
public class MongoViewSerializationTests
{
    private class Cust : AbstractModel { public string? Name { get; set; } }

    private class KeyedView { public Guid? EntityKey { get; set; } public string? Name { get; set; } }

    // A view property literally called Id — the driver's NamedIdMemberConvention maps this to element
    // _id, which the projection never emits (it emits "_id": 0 then the view property by name).
    private class IdNamedView { public Guid? Id { get; set; } public string? Name { get; set; } }

    private static ViewDefinition KeyedDefinition() => new ViewDefinitionBuilder<KeyedView>()
        .From<Cust>()
        .Select<Cust, Guid?>(c => c.Guid, v => v.EntityKey)
        .Select<Cust, string?>(c => c.Name, v => v.Name)
        .Build();

    private static ViewDefinition IdNamedDefinition() => new ViewDefinitionBuilder<IdNamedView>()
        .From<Cust>()
        .Select<Cust, Guid?>(c => c.Guid, v => v.Id)
        .Select<Cust, string?>(c => c.Name, v => v.Name)
        .Build();

    [Fact]
    public void A_view_property_carrying_the_canonical_id_is_string_represented()
    {
        // The projection rewrites the canonical Guid to $_id, and MongoSerialization stores that as a
        // string. Left to the framework's global binary GuidSerializer, the rendered filter would
        // compare BinData against a string and match nothing — measured as CountAsync returning 0
        // against a document that exists.
        MongoViewSerialization.EnsureRegistered<KeyedView>(KeyedDefinition());

        var member = BsonClassMap.LookupClassMap(typeof(KeyedView)).GetMemberMap(nameof(KeyedView.EntityKey));

        member.GetSerializer().Should().BeOfType<NullableSerializer<Guid>>();

        var rendered = Builders<KeyedView>.Filter
            .Where(v => v.EntityKey == new Guid("11111111-2222-3333-4444-555555555555"))
            .Render(new RenderArgs<KeyedView>(
                BsonSerializer.SerializerRegistry.GetSerializer<KeyedView>(),
                BsonSerializer.SerializerRegistry));

        rendered["EntityKey"].BsonType.Should().Be(BsonType.String,
            "the filter must compare against the same representation the projection emits");
        rendered["EntityKey"].AsString.Should().Be("11111111-2222-3333-4444-555555555555");
    }

    [Fact]
    public void A_view_has_no_id_member_and_its_elements_are_named_after_its_properties()
    {
        // The projection emits "_id": 0 and then each view field under its own property name, so a
        // view property called Id must NOT be mapped to _id. Left to the driver's convention this
        // threw FormatException("Element 'Id' does not match any field or property").
        MongoViewSerialization.EnsureRegistered<IdNamedView>(IdNamedDefinition());

        var map = BsonClassMap.LookupClassMap(typeof(IdNamedView));

        map.IdMemberMap.Should().BeNull("a view row is a projection, not an entity with identity");
        map.GetMemberMap(nameof(IdNamedView.Id)).ElementName.Should().Be("Id");

        var doc = new BsonDocument
        {
            { "Id", "11111111-2222-3333-4444-555555555555" },
            { "Name", "acme" },
        };
        var back = BsonSerializer.Deserialize<IdNamedView>(doc);
        back.Id.Should().Be(new Guid("11111111-2222-3333-4444-555555555555"));
        back.Name.Should().Be("acme");
    }

    [Fact]
    public void EnsureRegistered_is_idempotent_and_rejects_a_null_definition()
    {
        // Called from every MongoViewStore construction, so once per store instance, not once per type.
        var act = () =>
        {
            MongoViewSerialization.EnsureRegistered<KeyedView>(KeyedDefinition());
            MongoViewSerialization.EnsureRegistered<KeyedView>(KeyedDefinition());
        };
        act.Should().NotThrow();

        var nullAct = () => MongoViewSerialization.EnsureRegistered<KeyedView>(null!);
        nullAct.Should().Throw<ArgumentNullException>();
    }
}
