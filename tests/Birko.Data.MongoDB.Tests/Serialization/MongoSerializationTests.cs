using System;
using Birko.Data.Models;
using Birko.Data.MongoDB.Models;
using Birko.Data.MongoDB.Serialization;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Serializers;
using Xunit;

namespace Birko.Data.MongoDB.Tests.Serialization
{
    /// <summary>
    /// TASK-214 — until this fix, <b>no</b> Birko entity could be written to MongoDB at all. Two
    /// independent driver-level failures, both measured against MongoDB 7 with driver 3.2.0:
    /// <list type="number">
    /// <item><c>MongoDBModel</c> re-declared <c>public override Guid? Guid</c> to carry
    /// <c>[BsonRepresentation(BsonType.String)]</c>. <c>BsonClassMap</c> maps <i>declared</i> members
    /// per class, so the override and <c>AbstractModel.Guid</c> both claimed element name <c>Guid</c>
    /// and the map refused to freeze — the sync store's whole constraint type was unserializable.</item>
    /// <item>Driver 3.x removed <c>BsonDefaults.GuidRepresentation</c>; its default
    /// <c>GuidSerializer</c> carries <c>GuidRepresentation.Unspecified</c>, which throws instead of
    /// choosing. So the async store (constraint <c>AbstractModel</c>) failed on every write too —
    /// <c>CreateCoreAsync</c> always assigns a Guid, so the throwing path is unavoidable.</item>
    /// </list>
    /// <para>
    /// These tests are deliberately <b>non-gated</b>. The defect survived because the only suite that
    /// would have caught it, <c>MongoFilterMatrixLiveTests</c>, is gated on <c>BIRKO_MONGO_HOST</c> and
    /// no-ops without it — a capability that does not work at all, under a green suite. Class-mapping
    /// and BSON round-trip need no server, so nothing here is gated.
    /// </para>
    /// </summary>
    public class MongoSerializationTests
    {
        public MongoSerializationTests() => MongoSerialization.EnsureRegistered();

        public sealed class MgDoc : MongoDBModel { public string? Name { get; set; } }

        public sealed class PlainDoc : AbstractModel { public string? Name { get; set; } }

        public sealed class DocWithExtraGuid : MongoDBModel
        {
            // A bare Guid with no [BsonRepresentation] — the common shape on a consumer model, and
            // what GuidRepresentation.Unspecified refused to serialize.
            public Guid TenantGuid { get; set; }
        }

        private static readonly Guid Id = new Guid("11111111-2222-3333-4444-555555555555");

        [Fact]
        public void A_model_deriving_MongoDBModel_can_be_class_mapped()
        {
            // Failure 1: this threw BsonSerializationException("...cannot use element name 'Guid'...").
            var act = () => BsonSerializer.SerializerRegistry.GetSerializer<MgDoc>();

            act.Should().NotThrow();
        }

        [Fact]
        public void A_model_deriving_MongoDBModel_round_trips_through_bson()
        {
            var doc = new MgDoc { Guid = Id, Name = "x" }.ToBsonDocument();

            doc["Guid"].BsonType.Should().Be(BsonType.String, "the canonical id is stored as a string");
            doc["Guid"].AsString.Should().Be(Id.ToString());

            var back = BsonSerializer.Deserialize<MgDoc>(doc);
            back.Guid.Should().Be(Id);
            back.Name.Should().Be("x");
        }

        [Fact]
        public void A_model_deriving_AbstractModel_round_trips_through_bson()
        {
            // Failure 2 on the async store's constraint type: "GuidSerializer cannot serialize a Guid
            // when GuidRepresentation is Unspecified".
            var doc = new PlainDoc { Guid = Id, Name = "y" }.ToBsonDocument();

            doc["Guid"].AsString.Should().Be(Id.ToString());

            var back = BsonSerializer.Deserialize<PlainDoc>(doc);
            back.Guid.Should().Be(Id);
            back.Name.Should().Be("y");
        }

        [Fact]
        public void A_null_canonical_guid_round_trips_as_null()
        {
            var doc = new MgDoc { Guid = null, Name = "z" }.ToBsonDocument();

            doc["Guid"].IsBsonNull.Should().BeTrue();
            BsonSerializer.Deserialize<MgDoc>(doc).Guid.Should().BeNull();
        }

        [Fact]
        public void An_unattributed_guid_property_serializes_as_standard_binary()
        {
            // The global GuidSerializer, not the AbstractModel class map. Standard (subtype 4) is
            // required because ChangeStreamDocumentKeyResolver already reads a binary _id that way.
            var tenant = new Guid("99999999-8888-7777-6666-555555555555");

            var doc = new DocWithExtraGuid { Guid = Id, TenantGuid = tenant }.ToBsonDocument();

            var binary = doc["TenantGuid"].AsBsonBinaryData;
            binary.SubType.Should().Be(BsonBinarySubType.UuidStandard);
            binary.ToGuid().Should().Be(tenant);

            BsonSerializer.Deserialize<DocWithExtraGuid>(doc).TenantGuid.Should().Be(tenant);
        }

        [Fact]
        public void MongoDBModel_declares_no_members_of_its_own()
        {
            // Pins the shape of the fix rather than its effect: re-introducing a declared member that
            // shadows an AbstractModel one is exactly what made the class map unfreezable, and it does
            // so silently at the driver's first serialize rather than at compile time.
            typeof(MongoDBModel)
                .GetProperties(System.Reflection.BindingFlags.Public
                             | System.Reflection.BindingFlags.Instance
                             | System.Reflection.BindingFlags.DeclaredOnly)
                .Should().BeEmpty();
        }

        [Fact]
        public void EnsureRegistered_is_idempotent()
        {
            // The driver throws on a duplicate RegisterSerializer / RegisterClassMap, and this runs
            // from the MongoDBClient constructor — i.e. once per store, many times per process.
            var act = () => { MongoSerialization.EnsureRegistered(); MongoSerialization.EnsureRegistered(); };

            act.Should().NotThrow();
        }

        [Theory]
        [MemberData(nameof(GuidSerializerCases))]
        public void Only_the_drivers_throwing_default_counts_as_a_broken_registration(
            IBsonSerializer? existing, bool expectedBroken, string because)
        {
            // TryRegisterSerializer returning false has two causes that must NOT be conflated: a
            // consumer chose a serializer (honour it — documented first-wins precedence), or the
            // driver's own Unspecified default got cached because something resolved Guid before the
            // first store was built. Only the second re-creates the defect this class closes, so only
            // the second is refused. The throw itself is unreachable in-process once EnsureRegistered
            // has run — the registry caches for the process lifetime — so the decision is tested here.
            MongoSerialization.IsBrokenDefaultGuidSerializer(existing).Should().Be(expectedBroken, because);
        }

        public static TheoryData<IBsonSerializer?, bool, string> GuidSerializerCases() => new()
        {
            { new GuidSerializer(GuidRepresentation.Unspecified), true,
                "the driver's default throws on every Guid — nobody chooses that deliberately" },
            { new GuidSerializer(GuidRepresentation.Standard), false,
                "an explicit representation is a working choice, whoever made it" },
            { new GuidSerializer(BsonType.String), false, "string representation works too" },
            { null, false, "nothing registered is not a broken registration" },
        };

        [Fact]
        public void A_derived_model_cannot_opt_back_into_strict_extra_element_handling()
        {
            // Pinning a KNOWN COST of the fix, not endorsing it. IgnoreExtraElements is set on the
            // AbstractModel class map with IsInherited, because every real entity is a derived type
            // with its own automapped map and would otherwise throw on the driver-generated _id.
            // The driver's Freeze() then copies the base flag over the derived one unconditionally,
            // so [BsonIgnoreExtraElements(false)] on a model has no effect. Measured, not assumed.
            // This is a real conflict with the framework's "never drops it quietly" convention and
            // is why the alternative — mapping the canonical Guid AS _id, which removes the extra
            // element entirely — is filed as its own task rather than silently adopted here.
            var doc = new BsonDocument
            {
                { "Guid", Id.ToString() }, { "Name", "n" }, { "Unexpected", 1 },
            };

            BsonSerializer.Deserialize<StrictDoc>(doc).Name.Should().Be("n");
            BsonClassMap.LookupClassMap(typeof(StrictDoc)).IgnoreExtraElements.Should().BeTrue(
                "the base map's inherited flag overrides the derived attribute");
        }

        [BsonIgnoreExtraElements(false)]
        public sealed class StrictDoc : MongoDBModel { public string? Name { get; set; } }
    }
}
