using System;
using Birko.Data.Models;
using Birko.Data.Views;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Birko.Data.MongoDB.Views;

/// <summary>
/// Registers the driver class map for a view type so that it mirrors the aggregation projection
/// <see cref="MongoViewTranslator"/> emits.
/// </summary>
/// <remarks>
/// <para>
/// A view type is a <b>projection shape</b>, not an entity, and the driver's default conventions
/// assume the opposite. Two mismatches follow, both measured against MongoDB 7 (TASK-219), and both
/// affect the <c>$match</c> as much as the read-back — <c>MongoViewStore</c> renders its filter with
/// this same class map, so a serializer that disagrees with the projection produces a filter that
/// silently matches nothing rather than an error.
/// </para>
/// <list type="number">
/// <item>
/// <b>Representation.</b> A view property carrying an entity's canonical
/// <see cref="AbstractModel.Guid"/> receives the projected <c>_id</c>, which
/// <c>MongoSerialization</c> stores as a <b>string</b>. A <c>Guid</c>-typed view property otherwise
/// uses the framework's global binary <see cref="GuidSerializer"/>, so the rendered filter compares
/// binary against string: measured as <c>CountAsync(v =&gt; v.Key == id)</c> returning <b>0</b> for a
/// document that exists.
/// </item>
/// <item>
/// <b>Element naming.</b> The projection emits every view field under its <i>view property name</i>
/// and explicitly suppresses <c>_id</c>. The driver's <c>NamedIdMemberConvention</c> would otherwise
/// map a view property called <c>Id</c> to element <c>_id</c>, which the projection never produces —
/// measured as <c>FormatException: Element 'Id' does not match any field or property</c>. A view has
/// no identity of its own, so the id member is cleared and element names are pinned to the property
/// names the projection actually emits.
/// </item>
/// </list>
/// <para>
/// Registration is <c>TryRegisterClassMap</c>, so a consumer that mapped its own view type first
/// keeps that map — the same first-wins precedence <c>MongoSerialization</c> documents.
/// </para>
/// </remarks>
public static class MongoViewSerialization
{
    /// <summary>
    /// Ensures <typeparamref name="TView"/> is class-mapped to match <paramref name="definition"/>'s
    /// projection. Idempotent per type; safe to call from every view-store construction.
    /// </summary>
    public static void EnsureRegistered<TView>(ViewDefinition definition) where TView : class
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition));

        BsonClassMap.TryRegisterClassMap<TView>(cm =>
        {
            cm.AutoMap();

            // A view row has no identity — the projection emits "_id": 0 — so nothing here may be
            // treated as the document id.
            cm.SetIdMember(null);

            foreach (var member in cm.DeclaredMemberMaps)
            {
                member.SetElementName(member.MemberName);
            }

            foreach (var field in definition.Fields)
            {
                if (!IsCanonicalId(field)) continue;

                var member = cm.GetMemberMap(field.ViewProperty);
                if (member == null) continue;

                if (member.MemberType == typeof(Guid?))
                {
                    member.SetSerializer(new NullableSerializer<Guid>(new GuidSerializer(BsonType.String)));
                }
                else if (member.MemberType == typeof(Guid))
                {
                    member.SetSerializer(new GuidSerializer(BsonType.String));
                }
            }
        });
    }

    /// <summary>
    /// True when the field projects an entity's canonical id — the one field
    /// <see cref="MongoViewTranslator"/> rewrites to <c>_id</c>, and therefore the one whose stored
    /// representation is the string set by <c>MongoSerialization</c>.
    /// </summary>
    private static bool IsCanonicalId(FieldSelector field)
        => field.SourceProperty == nameof(AbstractModel.Guid)
           && typeof(AbstractModel).IsAssignableFrom(field.SourceType);
}
