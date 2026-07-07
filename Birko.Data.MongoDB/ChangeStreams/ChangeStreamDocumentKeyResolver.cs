using System;
using Birko.Data.Models;
using MongoDB.Bson;

namespace Birko.Data.MongoDB.ChangeStreams
{
    /// <summary>
    /// Resolves the framework <see cref="Guid"/> identity of a change-stream event.
    /// </summary>
    /// <remarks>
    /// The framework's canonical id (<see cref="AbstractModel.Guid"/>) is mapped with
    /// <c>[BsonRepresentation(BsonType.String)]</c> and, on <c>MongoDBModel</c>, is not marked
    /// <c>[BsonId]</c>, so the driver stores it as an ordinary string field and auto-generates an
    /// ObjectId for <c>_id</c>. The previous mapper only set the event key when
    /// <c>change.DocumentKey["_id"].IsGuid</c> was true — which is never the case for these models —
    /// leaving <see cref="ChangeStreamEvent{T}.DocumentKey"/> permanently null (CR-H072).
    /// This resolver:
    /// <list type="number">
    /// <item>reads a native BSON Guid <c>_id</c> (binary-represented models),</item>
    /// <item>parses a string <c>_id</c> back to a Guid (string-represented Guid ids),</item>
    /// <item>falls back to <c>FullDocument.Guid</c> when <c>_id</c> is not the Guid (the canonical
    /// <c>MongoDBModel</c> case, available for insert/update-with-lookup/replace).</item>
    /// </list>
    /// A delete event on a model whose <c>_id</c> is an ObjectId still cannot recover the Guid (no
    /// full document is delivered) — that is an inherent mapping limitation, not something the mapper
    /// can work around.
    /// </remarks>
    internal static class ChangeStreamDocumentKeyResolver
    {
        public static Guid? Resolve(BsonDocument? documentKey, AbstractModel? fullDocument)
        {
            if (documentKey != null && documentKey.Contains("_id"))
            {
                var idValue = documentKey["_id"];
                if (idValue.IsGuid)
                {
                    return idValue.AsGuid;
                }

                if (idValue.IsString && Guid.TryParse(idValue.AsString, out var parsed))
                {
                    return parsed;
                }
            }

            // _id is not the Guid (e.g. an auto-generated ObjectId): use the document's own Guid.
            if (fullDocument?.Guid is Guid docGuid && docGuid != Guid.Empty)
            {
                return docGuid;
            }

            return null;
        }
    }
}
