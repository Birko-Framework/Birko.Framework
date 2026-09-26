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
                    set => Builders<T>.Update.Set(name, BsonValue.Create(set.Value)),
                    increment =>
                    {
                        RefuseStringRepresentation<T>(name);
                        return Builders<T>.Update.Inc(name, BsonValue.Create(increment.Delta));
                    }));
            }
            return Builders<T>.Update.Combine(updateDefs);
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
