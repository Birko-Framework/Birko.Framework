using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Birko.Data.Stores;
using Birko.Data.Views;

namespace Birko.Data.RavenDB.Views;

/// <summary>
/// Translates a portable <see cref="ViewDefinition"/> into RavenDB index definition strings (Map/Reduce).
/// </summary>
public static class RavenViewTranslator
{
    /// <summary>
    /// Translates a <see cref="ViewDefinition"/> into a RavenDB Map/Reduce pair.
    /// Returns a tuple where <c>map</c> is always populated and <c>reduce</c> is null for non-aggregate views.
    /// </summary>
    public static (string map, string? reduce) TranslateToMapReduce(ViewDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var collectionName = definition.PrimarySource.Name;
        var map = BuildMapExpression(definition, collectionName);
        var reduce = definition.HasAggregates ? BuildReduceExpression(definition) : null;

        return (map, reduce);
    }

    private static string BuildMapExpression(ViewDefinition definition, string collectionName)
    {
        var sb = new StringBuilder();

        if (definition.HasJoins)
        {
            // Use LoadDocument pattern for joins
            var firstJoin = definition.Joins[0];
            var rightTypeName = firstJoin.RightType.Name;

            sb.Append($"from entity in docs.{collectionName}");
            sb.AppendLine();

            // Add LoadDocument lets for each join
            for (int i = 0; i < definition.Joins.Count; i++)
            {
                var join = definition.Joins[i];
                var joinAlias = i == 0 ? "joined" : $"joined{i + 1}";
                var joinRightName = join.RightType.Name;
                sb.AppendLine($"let {joinAlias} = LoadDocument<{joinRightName}>(entity.{join.LeftProperty})");
            }

            sb.Append("select new { ");
            sb.Append(BuildSelectFields(definition));
            sb.Append(" }");
        }
        else
        {
            // Simple map without joins
            sb.Append($"from entity in docs.{collectionName}");
            sb.AppendLine();
            sb.Append("select new { ");
            sb.Append(BuildSelectFields(definition));
            sb.Append(" }");
        }

        return sb.ToString();
    }

    private static string BuildSelectFields(ViewDefinition definition)
    {
        var parts = new List<string>();

        // Add regular fields
        foreach (var field in definition.Fields)
        {
            var sourceAlias = GetSourceAlias(field.SourceType, definition);
            parts.Add($"{field.ViewProperty} = {sourceAlias}.{field.SourceProperty}");
        }

        // Add aggregate fields for map phase (emit raw values for reduce)
        foreach (var agg in definition.Aggregates)
        {
            var sourceAlias = GetSourceAlias(agg.SourceType, definition);
            switch (agg.Function)
            {
                case AggregateFunction.Count:
                    parts.Add($"{agg.ViewProperty} = 1");
                    break;
                case AggregateFunction.Sum:
                case AggregateFunction.Avg:
                case AggregateFunction.Min:
                case AggregateFunction.Max:
                    if (agg.SourceProperty != null)
                    {
                        parts.Add($"{agg.ViewProperty} = {sourceAlias}.{agg.SourceProperty}");
                    }
                    break;
            }
        }

        // For Avg, emit a count helper field so reduce can compute the average
        var avgAggregates = definition.Aggregates.Where(a => a.Function == AggregateFunction.Avg).ToList();
        foreach (var avg in avgAggregates)
        {
            parts.Add($"{avg.ViewProperty}_Count = 1");
        }

        return string.Join(", ", parts);
    }

    private static string GetSourceAlias(Type sourceType, ViewDefinition definition)
    {
        if (sourceType == definition.PrimarySource)
        {
            return "entity";
        }

        // Find the join index for this type
        for (int i = 0; i < definition.Joins.Count; i++)
        {
            if (definition.Joins[i].RightType == sourceType)
            {
                return i == 0 ? "joined" : $"joined{i + 1}";
            }
        }

        // Fallback to entity if type is not found in joins
        return "entity";
    }

    private static string BuildReduceExpression(ViewDefinition definition)
    {
        var sb = new StringBuilder();

        // Build group by key
        var groupByFields = new List<string>();
        foreach (var grp in definition.GroupBy)
        {
            // Find the matching view property name for the group-by source property
            var viewProperty = FindViewPropertyForGroupBy(grp, definition);
            groupByFields.Add($"result.{viewProperty}");
        }

        // If no explicit group-by but we have fields, group by all non-aggregate fields
        if (groupByFields.Count == 0)
        {
            foreach (var field in definition.Fields)
            {
                groupByFields.Add($"result.{field.ViewProperty}");
            }
        }

        sb.Append("from result in results");
        sb.AppendLine();

        if (groupByFields.Count == 1)
        {
            sb.AppendLine($"group result by {groupByFields[0]} into g");
        }
        else
        {
            sb.Append("group result by new { ");
            sb.Append(string.Join(", ", groupByFields));
            sb.AppendLine(" } into g");
        }

        sb.Append("select new { ");

        var selectParts = new List<string>();

        // Emit grouped fields from key
        if (groupByFields.Count == 1)
        {
            var fieldName = groupByFields[0].Replace("result.", "");
            selectParts.Add($"{fieldName} = g.Key");
        }
        else
        {
            foreach (var grpField in groupByFields)
            {
                var fieldName = grpField.Replace("result.", "");
                selectParts.Add($"{fieldName} = g.Key.{fieldName}");
            }
        }

        // Emit aggregate fields
        foreach (var agg in definition.Aggregates)
        {
            switch (agg.Function)
            {
                case AggregateFunction.Count:
                    selectParts.Add($"{agg.ViewProperty} = g.Sum(x => x.{agg.ViewProperty})");
                    break;
                case AggregateFunction.Sum:
                    selectParts.Add($"{agg.ViewProperty} = g.Sum(x => x.{agg.ViewProperty})");
                    break;
                case AggregateFunction.Avg:
                    // Weighted average: Sum / Count
                    selectParts.Add($"{agg.ViewProperty} = g.Sum(x => x.{agg.ViewProperty}) / g.Sum(x => x.{agg.ViewProperty}_Count)");
                    selectParts.Add($"{agg.ViewProperty}_Count = g.Sum(x => x.{agg.ViewProperty}_Count)");
                    break;
                case AggregateFunction.Min:
                    selectParts.Add($"{agg.ViewProperty} = g.Min(x => x.{agg.ViewProperty})");
                    break;
                case AggregateFunction.Max:
                    selectParts.Add($"{agg.ViewProperty} = g.Max(x => x.{agg.ViewProperty})");
                    break;
            }
        }

        sb.Append(string.Join(", ", selectParts));
        sb.Append(" }");

        return sb.ToString();
    }

    private static string FindViewPropertyForGroupBy(GroupByClause grp, ViewDefinition definition)
    {
        // Try to find a matching field selector
        foreach (var field in definition.Fields)
        {
            if (field.SourceType == grp.SourceType && field.SourceProperty == grp.PropertyName)
            {
                return field.ViewProperty;
            }
        }

        // Fallback: use the property name directly
        return grp.PropertyName;
    }
}
