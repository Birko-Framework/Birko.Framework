using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Stores;
using Birko.Data.Views;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Birko.Data.MongoDB.Views;

/// <summary>
/// Translates a <see cref="ViewDefinition"/> into MongoDB aggregation pipeline stages.
/// </summary>
public static class MongoViewTranslator
{

    /// <summary>
    /// Translates a ViewDefinition into an ordered list of BsonDocument pipeline stages.
    /// Does not include $match (filter) — that is added at query time.
    /// </summary>
    public static List<BsonDocument> TranslatePipeline(ViewDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var stages = new List<BsonDocument>();

        // 1. $lookup stages for joins
        foreach (var join in definition.Joins)
        {
            var foreignCollection = GetCollectionName(join.RightType);
            var localField = GetFieldName(join.LeftProperty);
            var foreignField = GetFieldName(join.RightProperty);
            // TASK-151 (SH-M394): a Cross join was rendered as an equality join, a different row set from SQL's. A
            // $lookup on keys cannot be a cartesian product, and SQL refuses a cross join that carries keys
            // (TASK-626), so it is refused here too.
            if (join.JoinType == JoinType.Cross)
            {
                throw new NotSupportedException(
                    $"A Cross join of '{join.LeftType.Name}' and '{join.RightType.Name}' cannot be a MongoDB view: $lookup joins on keys. Use Inner or LeftOuter.");
            }

            var asField = "_joined_" + foreignCollection;

            stages.Add(new BsonDocument("$lookup", new BsonDocument
            {
                { "from", foreignCollection },
                { "localField", localField },
                { "foreignField", foreignField },
                { "as", asField }
            }));

            // $unwind to flatten the joined array (preserveNullAndEmptyArrays for LEFT OUTER)
            var unwindDoc = new BsonDocument("path", "$" + asField);
            if (join.JoinType == JoinType.LeftOuter)
            {
                unwindDoc.Add("preserveNullAndEmptyArrays", true);
            }
            stages.Add(new BsonDocument("$unwind", unwindDoc));
        }

        // 2. $group stage if aggregates are present
        if (definition.HasAggregates)
        {
            // Group keys serve both as the _id composite and as $first-carried-forward fields;
            // materialize once and reuse for both (they are the identical projection).
            var groupByPaths = definition.GroupBy
                .Select(grp => (grp.PropertyName, ResolveFieldPath(grp.SourceType, grp.PropertyName, definition)))
                .ToList();

            foreach (var agg in definition.Aggregates)
            {
                Aggregation.StoreAggregationHelper.ThrowIfViewExtremeUnordered(agg.SourceType, agg.SourceProperty, agg.Function,
                    $"MongoDB view {definition.Name ?? definition.ViewType.Name}");
            }
            var accumulators = definition.Aggregates
                .Select(agg => (
                    agg.ViewProperty,
                    agg.Function,
                    agg.Function == AggregateFunction.Count
                        ? (string?)null
                        : ResolveFieldPath(agg.SourceType, agg.SourceProperty!, definition)));

            var groupDoc = Aggregation.StoreAggregationHelper.BuildGroupStageFromPaths(
                groupByPaths, groupByPaths, accumulators);

            // TASK-907: a string Min / Max carries the two flags that find a group MongoDB's code-point order answers
            // otherwise than Ordinal; MongoViewStore refuses such a row by name and drops the flags.
            foreach (var agg in definition.Aggregates.Where(IsStringExtreme))
            {
                foreach (var (suffix, accumulator) in Aggregation.StoreAggregationHelper.StringOrderChecks("$" + ResolveFieldPath(agg.SourceType, agg.SourceProperty!, definition)))
                {
                    groupDoc.Add(Aggregation.StoreAggregationHelper.CheckName(CheckOwner(agg), suffix), accumulator);
                }
            }

            if (definition.HasGroupBy)
            {
                stages.Add(new BsonDocument("$group", groupDoc));
            }
            else
            {
                // TASK-886 (the user's decision): an ungrouped aggregate answers ONE row, also over no rows (Count 0, Sum 0,
                // the others null), on every provider. $group answers no document over no input (measured, on the fly and
                // as a persistent view); $facet always answers one, so the group runs inside it and an empty result is
                // replaced by that row.
                var empty = new BsonDocument();
                foreach (var agg in definition.Aggregates)
                {
                    empty.Add(agg.ViewProperty, agg.Function is AggregateFunction.Count or AggregateFunction.Sum ? 0 : BsonNull.Value);
                }
                stages.Add(new BsonDocument("$facet", new BsonDocument("rows", new BsonArray { new BsonDocument("$group", groupDoc) })));
                stages.Add(new BsonDocument("$replaceRoot", new BsonDocument("newRoot",
                    new BsonDocument("$ifNull", new BsonArray { new BsonDocument("$arrayElemAt", new BsonArray { "$rows", 0 }), empty }))));
            }
        }

        // 3. $project stage — map to view shape
        var project = new BsonDocument { { "_id", 0 } };

        foreach (var field in definition.Fields)
        {
            if (definition.HasAggregates)
            {
                // After $group, grouped fields are at root level
                project.Add(field.ViewProperty, "$" + field.SourceProperty);
            }
            else
            {
                var fieldPath = ResolveFieldPath(field.SourceType, field.SourcePath[0], definition);
                if (field.IsNested)
                {
                    fieldPath += "." + NestedElementPath(field);
                }
                project.Add(field.ViewProperty, "$" + fieldPath);
            }
        }

        foreach (var agg in definition.Aggregates)
        {
            // Aggregate fields are already at root after $group
            project.Add(agg.ViewProperty, 1);
        }
        foreach (var agg in definition.Aggregates.Where(IsStringExtreme))
        {
            foreach (var (suffix, _) in Aggregation.StoreAggregationHelper.StringOrderChecks("$x"))
            {
                project.Add(Aggregation.StoreAggregationHelper.CheckName(CheckOwner(agg), suffix), 1);
            }
        }

        if (project.ElementCount > 1) // more than just _id:0
        {
            stages.Add(new BsonDocument("$project", project));
        }

        return stages;
    }

    /// <summary>TASK-907: whether an aggregate is a Min / Max of a string source member.</summary>
    internal static bool IsStringExtreme(AggregateClause aggregate)
        => aggregate.Function is AggregateFunction.Min or AggregateFunction.Max && aggregate.SourceProperty != null
           && aggregate.SourceType.GetProperty(aggregate.SourceProperty)?.PropertyType == typeof(string);

    /// <summary>TASK-907: the owner name of a view aggregate's hidden checks.</summary>
    internal static string CheckOwner(AggregateClause aggregate) => "v_" + aggregate.ViewProperty;

    /// <summary>
    /// Returns the MongoDB collection name for a type (uses type name by convention).
    /// </summary>
    public static string GetCollectionName(Type type)
    {
        return type.Name;
    }

    /// <summary>
    /// Resolves the full field path, accounting for joined collections.
    /// </summary>
    private static string ResolveFieldPath(Type sourceType, string propertyName, ViewDefinition definition)
    {
        var fieldName = GetFieldName(propertyName);

        // If the source is the primary source, field is at root
        if (sourceType == definition.PrimarySource)
        {
            return fieldName;
        }

        // Otherwise it's from a joined collection
        var joinedAs = "_joined_" + GetCollectionName(sourceType);
        return joinedAs + "." + fieldName;
    }

    /// <summary>
    /// TASK-843: the element names below a nested source's first segment, <c>Sub.X</c> → <c>X</c>, so the projection
    /// reads <c>$Sub.X</c>. It used to read <c>$X</c>, the document's top-level field (measured on MongoDB 7). Each
    /// segment is named as the driver stores it: by the class map of the type that declares the member (its
    /// <c>[BsonElement]</c> name, or <c>_id</c> for an id member), registered or, when not registered yet, auto-mapped
    /// with the same conventions and not registered. A member the class map does not store is refused by name.
    /// </summary>
    private static string NestedElementPath(FieldSelector field)
    {
        var names = new List<string>();
        var owner = MemberType(field.SourceType, field.SourcePath[0], field);
        for (var i = 1; i < field.SourcePath.Count; i++)
        {
            var name = field.SourcePath[i];
            var member = (MemberInfo?)owner.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? owner.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            var declaring = member?.DeclaringType ?? owner;
            var classMap = BsonClassMap.IsClassMapRegistered(declaring) ? BsonClassMap.LookupClassMap(declaring) : AutoMapped(declaring);
            var memberMap = classMap.GetMemberMap(name) ?? throw new NotSupportedException(
                $"View field '{field.ViewProperty}' selects '{field.SourceType.Name}.{field.SourceProperty}', but '{declaring.Name}.{name}' "
                + "is not a member MongoDB stores (its class map does not map it). (TASK-843)");
            names.Add(ReferenceEquals(classMap.IdMemberMap, memberMap) ? "_id" : memberMap.ElementName);
            owner = MemberType(owner, name, field);
        }
        return string.Join(".", names);
    }

    private static Type MemberType(Type owner, string name, FieldSelector field)
    {
        var property = owner.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property != null) return property.PropertyType;
        var member = owner.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        return member?.FieldType ?? throw new NotSupportedException(
            $"View field '{field.ViewProperty}' selects '{field.SourceType.Name}.{field.SourceProperty}', but '{owner.Name}' has no public member '{name}'. (TASK-843)");
    }

    private static BsonClassMap AutoMapped(Type type)
    {
        var classMap = new BsonClassMap(type);
        classMap.AutoMap();
        return classMap;
    }

    /// <summary>
    /// Converts a C# property name to MongoDB field name.
    /// Maps Guid property to _id.
    /// </summary>
    private static string GetFieldName(string propertyName)
    {
        if (propertyName == "Guid")
        {
            return "_id";
        }

        return propertyName;
    }
}
