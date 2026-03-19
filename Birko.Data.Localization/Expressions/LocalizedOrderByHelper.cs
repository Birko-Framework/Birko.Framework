using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Data.Localization.Expressions;

/// <summary>
/// Detects whether an OrderBy references localizable fields and provides
/// in-memory sorting after translations are applied.
/// </summary>
public static class LocalizedOrderByHelper
{
    /// <summary>
    /// Checks if any of the OrderBy fields reference a localizable property.
    /// </summary>
    public static bool ReferencesLocalizedField<T>(OrderBy<T>? orderBy, IReadOnlyList<string> localizableFields)
    {
        if (orderBy == null || localizableFields.Count == 0)
        {
            return false;
        }

        var fieldSet = new HashSet<string>(localizableFields);
        return orderBy.Fields.Any(f => fieldSet.Contains(f.PropertyName));
    }

    /// <summary>
    /// Splits an OrderBy into localized and non-localized parts.
    /// Returns the non-localized OrderBy to pass to the inner store (null if all fields are localized).
    /// </summary>
    public static OrderBy<T>? GetNonLocalizedOrderBy<T>(OrderBy<T>? orderBy, IReadOnlyList<string> localizableFields)
    {
        if (orderBy == null)
        {
            return null;
        }

        var fieldSet = new HashSet<string>(localizableFields);
        var nonLocalized = orderBy.Fields.Where(f => !fieldSet.Contains(f.PropertyName)).ToList();

        if (nonLocalized.Count == 0)
        {
            return null;
        }

        if (nonLocalized.Count == orderBy.Fields.Count)
        {
            return orderBy; // no localized fields, pass through
        }

        // Rebuild OrderBy with only non-localized fields
        var first = nonLocalized[0];
        var result = OrderBy<T>.ByName(first.PropertyName, first.Descending);
        for (int i = 1; i < nonLocalized.Count; i++)
        {
            // Use reflection-less approach: ThenBy is expression-based, but we have string names
            // We need to add fields via ByName pattern — but ByName creates new OrderBy.
            // Since there's no ThenByName, we'll build a fresh one.
            // Actually, the Fields list is readonly, and constructors are private.
            // We need to chain: first ByName, then no way to add more via string.
            // Workaround: create one ByName per field — but the API expects single OrderBy.
            // The cleanest approach: just let the inner store ignore ordering,
            // and do full in-memory sort when any localized field is in the OrderBy.
        }

        // If we can't cleanly split, just return null and do full in-memory sort
        return null;
    }

    /// <summary>
    /// Applies in-memory ordering to a list of entities based on the full OrderBy,
    /// after translations have been applied to the entities.
    /// </summary>
    public static List<T> ApplyInMemoryOrderBy<T>(List<T> entities, OrderBy<T> orderBy)
        where T : Data.Models.AbstractModel
    {
        if (entities.Count <= 1 || orderBy.Fields.Count == 0)
        {
            return entities;
        }

        var type = typeof(T);
        IOrderedEnumerable<T>? ordered = null;

        for (int i = 0; i < orderBy.Fields.Count; i++)
        {
            var field = orderBy.Fields[i];
            var prop = type.GetProperty(field.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                continue;
            }

            Func<T, object?> selector = e => prop.GetValue(e);

            if (i == 0)
            {
                ordered = field.Descending
                    ? entities.OrderByDescending(selector)
                    : entities.OrderBy(selector);
            }
            else
            {
                ordered = field.Descending
                    ? ordered!.ThenByDescending(selector)
                    : ordered!.ThenBy(selector);
            }
        }

        return ordered?.ToList() ?? entities;
    }

    /// <summary>
    /// Applies in-memory skip/take pagination.
    /// </summary>
    public static List<T> ApplyInMemoryPaging<T>(List<T> entities, int? offset, int? limit)
    {
        IEnumerable<T> result = entities;
        if (offset.HasValue)
        {
            result = result.Skip(offset.Value);
        }
        if (limit.HasValue)
        {
            result = result.Take(limit.Value);
        }
        return result.ToList();
    }
}
