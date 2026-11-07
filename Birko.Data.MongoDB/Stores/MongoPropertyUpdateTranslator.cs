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
            if (updates.Assignments.Any(a => a.Match(set => false, increment => false, computed => true)))
            {
                return BuildPipeline(updates);
            }

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
                    },
                    computed => throw new InvalidOperationException("A computed set is translated as a pipeline.")));
            }
            return Builders<T>.Update.Combine(updateDefs);
        }

        /// <summary>
        /// TASK-826 — an update holding a computed set, as one pipeline update (MongoDB 4.2+): a single <c>$set</c> stage
        /// whose expressions all read the document as it was before the update, as a SQL UPDATE does. A constant is a
        /// <c>$literal</c>, an increment <c>$add</c>, a formula the aggregation operators over Decimal128, which keeps
        /// 34 significant digits.
        /// </summary>
        private static UpdateDefinition<T> BuildPipeline<T>(PropertyUpdate<T> updates) where T : Data.Models.AbstractModel
        {
            var stage = new BsonDocument();
            foreach (var assignment in updates.Assignments)
            {
                var name = assignment.PropertyInfo?.Name
                    ?? throw new ArgumentException($"Unable to resolve property from expression: {assignment.Property}");
                stage[ElementName<T>(name)] = assignment.Match<BsonValue>(
                    set => new BsonDocument("$literal", ToBsonValue<T>(name, set.Value)),
                    increment =>
                    {
                        RefuseStringRepresentation<T>(name);
                        return new BsonDocument("$add", new BsonArray { "$" + ElementName<T>(name), BsonValue.Create(increment.Delta) });
                    },
                    computed =>
                    {
                        RefuseStringRepresentation<T>(name);
                        return Render<T>(computed.Value);
                    });
            }

            var pipeline = PipelineDefinition<T, T>.Create(new BsonDocument("$set", stage));
            return Builders<T>.Update.Pipeline(pipeline);
        }

        private static BsonValue Render<T>(ComputedValue value) => value.Match<BsonValue>(
            column =>
            {
                RefuseStringRepresentation<T>(column.Property.Name);
                return "$" + ElementName<T>(column.Property.Name);
            },
            constant => new BsonDecimal128(constant.Value),
            binary => new BsonDocument(binary.Operator switch
            {
                ComputedOperator.Add => "$add",
                ComputedOperator.Subtract => "$subtract",
                ComputedOperator.Multiply => "$multiply",
                ComputedOperator.Divide => "$divide",
                _ => throw new NotSupportedException($"Operator {binary.Operator} is not supported."),
            }, new BsonArray { Render<T>(binary.Left), Render<T>(binary.Right) }),
            round => Round(Render<T>(round.Operand), round.Digits, round.Mode));

        /// <summary>
        /// <c>$round</c> rounds half to even. Half away from zero is
        /// <c>sign(v) * floor(|v| * 10^d + 0.5) / 10^d</c>, every constant a Decimal128 so nothing passes through a double.
        /// </summary>
        private static BsonValue Round(BsonValue value, int digits, MidpointRounding mode)
        {
            if (mode == MidpointRounding.ToEven)
            {
                return new BsonDocument("$round", new BsonArray { value, digits });
            }

            var scale = 1m;
            for (var i = 0; i < digits; i++) scale *= 10m;
            var magnitude = new BsonDocument("$floor", new BsonDocument("$add", new BsonArray
            {
                new BsonDocument("$multiply", new BsonArray { new BsonDocument("$abs", "$$v"), new BsonDecimal128(scale) }),
                new BsonDecimal128(0.5m),
            }));
            var signed = new BsonDocument("$cond", new BsonArray
            {
                new BsonDocument("$lt", new BsonArray { "$$v", new BsonDecimal128(0m) }),
                new BsonDocument("$multiply", new BsonArray { magnitude, new BsonDecimal128(-1m) }),
                magnitude,
            });
            return new BsonDocument("$let", new BsonDocument
            {
                { "vars", new BsonDocument("v", value) },
                { "in", new BsonDocument("$multiply", new BsonArray { signed, new BsonDecimal128(1m / scale) }) },
            });
        }

        /// <summary>The element a member is stored under, as its class map names it.</summary>
        private static string ElementName<T>(string member)
            => BsonClassMap.LookupClassMap(typeof(T)).AllMemberMaps.FirstOrDefault(m => m.MemberName == member)?.ElementName ?? member;

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
                $"Cannot do arithmetic on '{typeof(T).Name}.{name}' (an increment or a computed set): it is stored as a string, "
                + "and MongoDB's $inc and arithmetic operators only work on numbers. "
                + "Store it numerically, e.g. [BsonRepresentation(BsonType.Decimal128)] for a decimal.");
        }
    }
}
