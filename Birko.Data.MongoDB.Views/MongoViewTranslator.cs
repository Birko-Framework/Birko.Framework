using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Views;
using MongoDB.Bson;

namespace Birko.Data.MongoDB.Views;

/// <summary>
/// Translates a <see cref="ViewDefinition"/> into MongoDB aggregation pipeline stages.
/// </summary>
public static class MongoViewTranslator
{
    private static readonly Dictionary<AggregateFunction, string> AggregateOperators = new()
    {
        [AggregateFunction.Count] = "$sum",
        [AggregateFunction.Sum] = "$sum",
        [AggregateFunction.Avg] = "$avg",
        [AggregateFunction.Min] = "$min",
        [AggregateFunction.Max] = "$max"
    };

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
            var groupId = new BsonDocument();
            foreach (var grp in definition.GroupBy)
            {
                var fieldPath = ResolveFieldPath(grp.SourceType, grp.PropertyName, definition);
                groupId.Add(grp.PropertyName, "$" + fieldPath);
            }

            var groupDoc = new BsonDocument("_id", groupId.ElementCount > 0 ? (BsonValue)groupId : BsonNull.Value);

            // Add grouped fields to carry them forward
            foreach (var grp in definition.GroupBy)
            {
                var fieldPath = ResolveFieldPath(grp.SourceType, grp.PropertyName, definition);
                groupDoc.Add(grp.PropertyName, new BsonDocument("$first", "$" + fieldPath));
            }

            // Add aggregate expressions
            foreach (var agg in definition.Aggregates)
            {
                var op = AggregateOperators[agg.Function];

                BsonValue aggExpr;
                if (agg.Function == AggregateFunction.Count)
                {
                    aggExpr = new BsonDocument(op, 1);
                }
                else
                {
                    var fieldPath = ResolveFieldPath(agg.SourceType, agg.SourceProperty!, definition);
                    aggExpr = new BsonDocument(op, "$" + fieldPath);
                }

                groupDoc.Add(agg.ViewProperty, aggExpr);
            }

            stages.Add(new BsonDocument("$group", groupDoc));
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
                var fieldPath = ResolveFieldPath(field.SourceType, field.SourceProperty, definition);
                project.Add(field.ViewProperty, "$" + fieldPath);
            }
        }

        foreach (var agg in definition.Aggregates)
        {
            // Aggregate fields are already at root after $group
            project.Add(agg.ViewProperty, 1);
        }

        if (project.ElementCount > 1) // more than just _id:0
        {
            stages.Add(new BsonDocument("$project", project));
        }

        return stages;
    }

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
    /// Converts a C# property name to MongoDB field name convention.
    /// MongoDB uses camelCase by default; Guid property maps to _id.
    /// </summary>
    private static string GetFieldName(string propertyName)
    {
        if (propertyName == "Guid")
        {
            return "_id";
        }

        // Convert to camelCase
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        return char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
    }
}
