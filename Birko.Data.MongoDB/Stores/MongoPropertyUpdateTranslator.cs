using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Stores;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Birko.Data.MongoDB.Stores
{
    /// <summary>
    /// Renders a <see cref="PropertyUpdate{T}"/> as one combined update definition (<c>$set</c> / <c>$inc</c>).
    /// Shared by the sync and async stores so the two cannot drift.
    /// </summary>
    internal static class MongoPropertyUpdateTranslator
    {
        internal static UpdateDefinition<T> Build<T>(PropertyUpdate<T> updates) where T : Data.Models.AbstractModel
        {
            var updateDefs = new List<UpdateDefinition<T>>();
            foreach (var assignment in updates.Assignments)
            {
                var name = assignment.PropertyInfo?.Name
                    ?? throw new ArgumentException($"Unable to resolve property from expression: {assignment.Property}");

                updateDefs.Add(assignment.Match(
                    set => Builders<T>.Update.Set(name, ToBsonValue<T>(name, set.Value)),
                    increment =>
                    {
                        RefuseStringRepresentation<T>(name);
                        return Builders<T>.Update.Inc(name, BsonValue.Create(increment.Delta));
                    }));
            }
            return Builders<T>.Update.Combine(updateDefs);
        }

        /// <summary>
        /// A value as the member's own serializer writes it (TASK-662). <see cref="BsonValue.Create(object)"/> knows no
        /// representation, so a <see cref="Guid"/> threw ("cannot be mapped to a BsonValue") under driver 3.x, and every
        /// <c>$set</c> of a Guid failed — the MongoDB job queue's claim among them, so it could not dequeue on a real server.
        /// The member's serializer applies the representation the document itself is written with.
        /// </summary>
        private static BsonValue ToBsonValue<T>(string name, object? value)
        {
            if (value == null)
            {
                return BsonNull.Value;
            }

            var memberMap = BsonClassMap.LookupClassMap(typeof(T)).AllMemberMaps.FirstOrDefault(m => m.MemberName == name);
            var serializer = memberMap?.GetSerializer() ?? BsonSerializer.LookupSerializer(value.GetType());
            var holder = new BsonDocument();
            using (var writer = new global::MongoDB.Bson.IO.BsonDocumentWriter(holder))
            {
                writer.WriteStartDocument();
                writer.WriteName("v");
                serializer.Serialize(BsonSerializationContext.CreateRoot(writer), new BsonSerializationArgs(), value);
                writer.WriteEndDocument();
            }
            return holder["v"];
        }

        /// <summary>
        /// <c>$inc</c> on a field stored as a string fails at the server, so a member configured with a string
        /// representation (e.g. <c>[BsonRepresentation(BsonType.String)]</c> on a <c>decimal</c>) is refused here, before
        /// anything is sent, naming the fix. The driver's own default for <c>decimal</c> is Decimal128 (measured on
        /// MongoDB.Bson 3.12), which increments normally.
        /// </summary>
        private static void RefuseStringRepresentation<T>(string name)
        {
            var memberMap = BsonClassMap.LookupClassMap(typeof(T)).AllMemberMaps.FirstOrDefault(m => m.MemberName == name);
            if (memberMap?.GetSerializer() is not IRepresentationConfigurable { Representation: BsonType.String })
            {
                return;
            }

            throw new NotSupportedException(
                $"Cannot increment '{typeof(T).Name}.{name}': it is stored as a string, and MongoDB's $inc only works on numbers. "
                + "Store it numerically, e.g. [BsonRepresentation(BsonType.Decimal128)] for a decimal.");
        }
    }
}
