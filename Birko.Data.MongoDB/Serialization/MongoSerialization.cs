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
    /// <b>The canonical <see cref="AbstractModel.Guid"/> IS <c>_id</c></b> (TASK-219). One identity per
    /// document, stored as a string. The framework briefly held two contradictory answers here — this
    /// registration said <c>_id</c> was a driver-generated ObjectId while
    /// <c>MongoViewTranslator.GetFieldName</c> mapped the <c>Guid</c> property to <c>_id</c> — and under
    /// the former, every view projecting the canonical id threw and every view filtering on it returned
    /// <i>silently zero</i> rows. Mapping the id here settles it in the translator's favour and removes
    /// the <c>IgnoreExtraElements</c> that tolerating a second id had required.
    /// </para>
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

            // The canonical Guid IS _id (TASK-219). There is exactly one identity per document,
            // stored as a string, and no driver-generated ObjectId beside it.
            //
            // The alternative — leaving _id to the driver and keeping Guid as an ordinary field —
            // is what TASK-214 shipped, and it cost an IgnoreExtraElements(IsInherited) on this map
            // purely to stop the unwanted ObjectId breaking every read. That turned every Birko
            // entity into a silent-drop reader with no per-model opt-out, against the framework's
            // own "never drops it quietly" rule, and it left MongoViewTranslator — which maps the
            // Guid property to _id — projecting the ObjectId: measured as a throw on a Guid-typed
            // view property, and as a silently empty result when filtering on it.
            cm.SetIdMember(guid);
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
        /// True when <paramref name="map"/> already agrees with the framework about identity: the
        /// canonical <see cref="AbstractModel.Guid"/> is the document's <c>_id</c>. Either a consumer
        /// map that reached the same conclusion, or this class's own map from an earlier call.
        /// </summary>
        internal static bool MapsTheCanonicalId(BsonClassMap? map)
            => map?.IdMemberMap is { } id && id.MemberName == nameof(AbstractModel.Guid);

        /// <summary>
        /// Called when <c>TryRegisterClassMap</c> refused because a class map for
        /// <see cref="AbstractModel"/> already exists. Same two causes as the serializer above.
        /// </summary>
        private static void VerifyExistingAbstractModelClassMap()
        {
            if (MapsTheCanonicalId(BsonClassMap.LookupClassMap(typeof(AbstractModel)))) return;

            throw new InvalidOperationException(
                "MongoDB serialization could not be registered: a class map for AbstractModel was "
                + "already frozen before the first MongoDBClient was constructed, and it does not map "
                + "the canonical Guid as the document _id — so reads and writes of Birko entities will "
                + "not agree on identity. Call MongoSerialization.EnsureRegistered() during application "
                + "start-up, before any AbstractModel-derived type is serialized.");
        }
    }
}
