using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Birko.Data.Stores;
using Birko.Data.SQL;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Fields;
using Birko.Data.SQL.Tables;
using Birko.Data.Views;
using PortableJoinType = Birko.Data.Views.JoinType;
using PortableViewQueryMode = Birko.Data.Views.ViewQueryMode;

namespace Birko.Data.SQL.Views;

/// <summary>
/// Translates a portable <see cref="ViewDefinition"/> into the SQL-specific
/// <see cref="Table"/> view metadata that the existing connector infrastructure expects.
/// </summary>
public static class SqlViewTranslator
{

    /// <summary>
    /// Translates a <see cref="ViewDefinition"/> into a SQL <see cref="View"/>.
    /// </summary>
    public static Tables.View Translate(ViewDefinition definition)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        // CR-M153: this backend derives GROUP BY from the SELECTed non-aggregate fields
        // (AbstractConnectorBase_View.BuildViewSelectSql), so a GroupBy field that is not also
        // Select()ed would be silently dropped from the grouping, producing wrong aggregate results
        // with no error. Reject it explicitly. (The reverse — a selected non-aggregate field missing
        // from GroupBy — is already rejected by ViewDefinitionBuilder.Build.)
        foreach (var grp in definition.GroupBy)
        {
            var isSelected = definition.Fields.Any(field =>
                field.SourceType == grp.SourceType && field.SourceProperty == grp.PropertyName);
            if (!isSelected)
            {
                throw new NotSupportedException(
                    $"GroupBy field '{grp.SourceType.Name}.{grp.PropertyName}' must also be selected via Select(): " +
                    "the SQL view backend derives GROUP BY from the projected non-aggregate fields, so a group-by " +
                    "field that is not part of the view's SELECT list cannot be honored.");
            }
        }

        var view = new Tables.View();
        view.Name = definition.Name;
        view.QueryMode = TranslateQueryMode(definition.QueryMode);

        // Collect all source types referenced in the definition
        var sourceTypes = CollectSourceTypes(definition);
        var tableCache = new Dictionary<Type, Table>();

        foreach (var sourceType in sourceTypes)
        {
            var table = DataBase.LoadTable(sourceType);
            if (table != null)
            {
                tableCache[sourceType] = table;
            }
        }

        // Add selected fields
        foreach (var field in definition.Fields)
        {
            if (!tableCache.TryGetValue(field.SourceType, out var table))
            {
                continue;
            }

            var sourceField = table.GetFieldByPropertyName(field.SourceProperty);
            if (sourceField == null)
            {
                continue;
            }

            // Create a field copy with the view property info
            var viewProp = definition.ViewType.GetProperty(field.ViewProperty,
                BindingFlags.Public | BindingFlags.Instance);
            if (viewProp == null)
            {
                continue;
            }

            var loadedFields = DataBase.LoadField(sourceField.Property);
            foreach (var loadField in loadedFields)
            {
                loadField.Property = viewProp;
                view.AddField(table.Name, table.Type, loadField);
            }
        }

        // Add aggregate fields
        foreach (var agg in definition.Aggregates)
        {
            if (!tableCache.TryGetValue(agg.SourceType, out var table))
            {
                continue;
            }

            var viewProp = definition.ViewType.GetProperty(agg.ViewProperty,
                BindingFlags.Public | BindingFlags.Instance);
            if (viewProp == null)
            {
                continue;
            }

            var sqlFuncName = SQL.Connectors.AbstractConnectorBase.GetSqlFunctionName(agg.Function);

            if (agg.SourceProperty == null)
            {
                // COUNT(*) — use first field from the table as base
                var firstField = table.Fields?.Values.FirstOrDefault();
                if (firstField == null)
                {
                    continue;
                }

                var countField = FunctionField.CreateFunctionField(viewProp, sqlFuncName, firstField);
                if (countField != null)
                {
                    view.AddField(table.Name, table.Type, countField, countField.Name);
                }
            }
            else
            {
                var sourceField = table.GetFieldByPropertyName(agg.SourceProperty);
                if (sourceField == null)
                {
                    continue;
                }

                var functionField = FunctionField.CreateFunctionField(viewProp, sqlFuncName, sourceField);
                if (functionField != null)
                {
                    view.AddField(table.Name, table.Type, functionField, functionField.Name);
                }
            }
        }

        // Add joins
        foreach (var join in definition.Joins)
        {
            if (!tableCache.TryGetValue(join.LeftType, out var leftTable) ||
                !tableCache.TryGetValue(join.RightType, out var rightTable))
            {
                continue;
            }

            var leftField = leftTable.GetFieldByPropertyName(join.LeftProperty);
            var rightField = rightTable.GetFieldByPropertyName(join.RightProperty);
            if (leftField == null || rightField == null)
            {
                continue;
            }

            var sqlJoinType = TranslateJoinType(join.JoinType);
            var condition = Condition.AndField(
                leftTable.Name + "." + leftField.Name,
                rightTable.Name + "." + rightField.Name);

            view.AddJoin(Join.Create(leftTable.Name, rightTable.Name, condition, sqlJoinType));
        }

        // Derive name from table names if not explicitly set
        if (string.IsNullOrEmpty(view.Name) && view.Tables != null && view.Tables.Any())
        {
            view.Name = string.Join(string.Empty,
                view.Tables.Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).Distinct());
        }

        return view;
    }

    private static HashSet<Type> CollectSourceTypes(ViewDefinition definition)
    {
        var types = new HashSet<Type> { definition.PrimarySource };

        foreach (var field in definition.Fields)
        {
            types.Add(field.SourceType);
        }

        foreach (var join in definition.Joins)
        {
            types.Add(join.LeftType);
            types.Add(join.RightType);
        }

        foreach (var agg in definition.Aggregates)
        {
            types.Add(agg.SourceType);
        }

        foreach (var grp in definition.GroupBy)
        {
            types.Add(grp.SourceType);
        }

        return types;
    }

    private static Conditions.JoinType TranslateJoinType(PortableJoinType joinType)
    {
        return joinType switch
        {
            PortableJoinType.Inner => Conditions.JoinType.Inner,
            PortableJoinType.LeftOuter => Conditions.JoinType.LeftOuter,
            PortableJoinType.Cross => Conditions.JoinType.Cross,
            _ => Conditions.JoinType.Cross
        };
    }

    private static Birko.Data.SQL.ViewQueryMode TranslateQueryMode(PortableViewQueryMode mode)
    {
        return mode switch
        {
            PortableViewQueryMode.OnTheFly => Birko.Data.SQL.ViewQueryMode.OnTheFly,
            PortableViewQueryMode.Persistent => Birko.Data.SQL.ViewQueryMode.Persistent,
            PortableViewQueryMode.Auto => Birko.Data.SQL.ViewQueryMode.Auto,
            _ => Birko.Data.SQL.ViewQueryMode.OnTheFly
        };
    }

}
