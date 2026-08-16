using System;
using Birko.Data.Models;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Birko.Data.MongoDB.Serialization
{
    /// <summary>
    /// The framework's one-time registration of MongoDB driver serialization for
    /// <see cref="AbstractModel"/>-derived entities.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driver 3.x ships no usable default for either half of this. Without the registration below,
    /// <b>no</b> Birko entity can be written to MongoDB at all (TASK-214, measured against MongoDB 7):
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>The duplicate <c>Guid</c> element.</b> <c>MongoDBModel</c> used to re-declare
    /// <c>public override Guid? Guid</c> purely to carry <c>[BsonRepresentation(BsonType.String)]</c>.
    /// <c>BsonClassMap</c> maps <i>declared</i> members per class in the hierarchy, so the override and
    /// <see cref="AbstractModel.Guid"/> both claimed element name <c>Guid</c> and the map refused to
    /// freeze — the sync store's entire constraint type was unserializable. The override is gone; the
    /// string representation it existed for is applied here instead, on the class map of the type that
    /// actually declares the member.
    /// </item>
    /// <item>
    /// <b>The Guid representation.</b> Driver 3.x removed <c>BsonDefaults.GuidRepresentation</c> and
    /// its default <see cref="GuidSerializer"/> carries <see cref="GuidRepresentation.Unspecified"/>,
    /// which <i>throws</i> rather than picking one. Any un-attributed <see cref="Guid"/> on a consumer
    /// model therefore fails to serialize. <see cref="GuidRepresentation.Standard"/> is chosen because
    /// <c>ChangeStreamDocumentKeyResolver</c> already assumes it when reading a binary <c>_id</c>.
    /// </item>
    /// </list>
    /// <para>
    /// <b>Consumer configuration wins.</b> Both calls are <c>TryRegister*</c>, and this runs from the
    /// <see cref="MongoDBClient"/> constructor — i.e. when a store is given its settings, which is after
    /// application start-up. A consumer that registers its own Guid serializer or its own
    /// <see cref="AbstractModel"/> class map before constructing a store keeps it; the framework only
    /// fills in what nobody chose. That precedence is the reason this is not a module initializer, which
    /// would run first and silently override the consumer.
    /// </para>
    /// </remarks>
    public static class MongoSerialization
    {
        private static readonly object _gate = new object();

        // volatile: the fast-path read below is outside the lock, and the XML doc claims thread
        // safety. The rest of the framework spells this pattern with a plain bool
        // (AbstractStore._initialized) and gets away with it because it publishes no state a
        // caller reads; this one publishes driver-registry state, so on a weakly-ordered target a
        // second thread could see the flag before the registrations. One word to make the claim true.
        private static volatile bool _registered;

        /// <summary>
        /// Registers the framework's driver serialization defaults, once per process.
        /// Idempotent and thread-safe; safe to call before constructing a store or a client.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The driver already resolved a <see cref="Guid"/> serializer or an
        /// <see cref="AbstractModel"/> class map, and what it resolved is the broken default rather
        /// than a deliberate consumer choice — see the remarks on <see cref="MongoSerialization"/>.
        /// </exception>
        public static void EnsureRegistered()
        {
            if (_registered) return;

            lock (_gate)
            {
                if (_registered) return;

                // A bare Guid/Guid? with no [BsonRepresentation] — the common case on consumer models.
                if (!BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard)))
                {
                    VerifyExistingGuidSerializer();
                }

                // The framework's canonical id, stored as a string. This is what MongoDBModel's
                // [BsonRepresentation(BsonType.String)] override used to express before it made the
                // class map unfreezable.
                if (!BsonClassMap.TryRegisterClassMap<AbstractModel>(ConfigureAbstractModel))
                {
                    VerifyExistingAbstractModelClassMap();
                }

                _registered = true;
            }
        }

        private static void ConfigureAbstractModel(BsonClassMap<AbstractModel> cm)
        {
            cm.AutoMap();

            var guid = cm.GetMemberMap(x => x.Guid)
                ?? throw new InvalidOperationException(
                    "MongoDB serialization cannot be registered: AutoMap did not map "
                    + "AbstractModel.Guid, so the framework's canonical id has no member map to "
                    + "configure. A convention pack registered before the first store was "
                    + "constructed is the usual cause — check for a member-filtering convention.");

            guid.SetSerializer(new NullableSerializer<Guid>(new GuidSerializer(BsonType.String)));

            // No Birko model declares _id, by design: the canonical id is an ordinary Guid
            // field and the driver auto-generates an ObjectId for _id (the assumption
            // ChangeStreamDocumentKeyResolver is written around). Without this the write
            // succeeds and the READ throws
            // FormatException("Element '_id' does not match any field or property").
            // IsInherited, because a derived model gets its own automapped class map and the
            // flag is not inherited by default — the entity types are all derived.
            cm.SetIgnoreExtraElements(true);
            cm.SetIgnoreExtraElementsIsInherited(true);
        }

        /// <summary>
        /// True when <paramref name="existing"/> is the driver's own broken default rather than a
        /// deliberate consumer choice. Split out and <c>internal</c> because the branch it guards is
        /// unreachable in-process once <see cref="EnsureRegistered"/> has run (the registry caches
        /// for the life of the process), so the decision is tested here even though the throw is not.
        /// </summary>
        /// <remarks>
        /// <c>GuidRepresentation</c> alone is NOT the test, and assuming it was is a false refusal
        /// this check's own test caught: <c>new GuidSerializer(BsonType.String)</c> also reports
        /// <c>Unspecified</c> and serializes perfectly well, because the representation only selects
        /// among the <i>binary</i> encodings. Broken means binary-encoded with no encoding chosen.
        /// </remarks>
        internal static bool IsBrokenDefaultGuidSerializer(IBsonSerializer? existing)
            => existing is GuidSerializer g
               && g.Representation == BsonType.Binary
               && g.GuidRepresentation == GuidRepresentation.Unspecified;

        /// <summary>
        /// Called when <c>TryRegisterSerializer</c> refused because something already owns the
        /// <see cref="Guid"/> serializer. Distinguishes the two causes, which are NOT interchangeable.
        /// </summary>
        private static void VerifyExistingGuidSerializer()
        {
            // A consumer's own choice: honour it (documented first-wins precedence) and say nothing.
            if (!IsBrokenDefaultGuidSerializer(BsonSerializer.LookupSerializer<Guid>())) return;

            // The driver's own default, cached because something resolved Guid before the first
            // store was constructed. Nobody chooses a serializer that throws on every value, so
            // this is never a deliberate opt-out — and leaving it would re-create the exact defect
            // this class exists to close, minus the diagnostic.
            throw new InvalidOperationException(
                "MongoDB serialization could not be registered: the driver's default GuidSerializer "
                + "(GuidRepresentation.Unspecified, which throws on every Guid) was already resolved "
                + "and cached before the first MongoDBClient was constructed, so the framework's "
                + "registration was refused. Call MongoSerialization.EnsureRegistered() during "
                + "application start-up, before anything serializes BSON, or register your own "
                + "GuidSerializer with an explicit representation.");
        }

        /// <summary>
        /// Called when <c>TryRegisterClassMap</c> refused because a class map for
        /// <see cref="AbstractModel"/> already exists. Same two causes as the serializer above.
        /// </summary>
        private static void VerifyExistingAbstractModelClassMap()
        {
            var existing = BsonClassMap.LookupClassMap(typeof(AbstractModel));

            // Anything that already string-represents the canonical id is a consumer map that
            // agrees with the framework, or the framework's own map from an earlier call.
            var guid = existing.GetMemberMap(nameof(AbstractModel.Guid));
            if (guid?.GetSerializer() is NullableSerializer<Guid>) return;
            if (existing.IgnoreExtraElements) return;

            throw new InvalidOperationException(
                "MongoDB serialization could not be registered: a class map for AbstractModel was "
                + "already frozen before the first MongoDBClient was constructed, and it maps neither "
                + "the canonical Guid as a string nor tolerates the driver-generated _id — so reads "
                + "and writes of Birko entities will fail. Call MongoSerialization.EnsureRegistered() "
                + "during application start-up, before any AbstractModel-derived type is serialized.");
        }
    }
}
