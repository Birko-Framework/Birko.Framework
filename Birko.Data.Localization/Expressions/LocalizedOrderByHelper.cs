using Birko.Data.Localization.Models;
using Birko.Data.Stores;
using Birko.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Applies in-memory ordering to a list of entities based on the full OrderBy,
    /// after translations have been applied to the entities.
    /// </summary>
    /// <param name="culture">
    /// The culture the translated text is in. Strings are collated with it (SH-M080): the thread culture,
    /// often invariant on a server, mis-orders letters such as Slovak "č". <c>null</c> keeps the previous
    /// behaviour of comparing with the thread culture.
    /// </param>
    public static List<T> ApplyInMemoryOrderBy<T>(List<T> entities, OrderBy<T> orderBy, CultureInfo? culture = null)
        where T : Data.Models.AbstractModel
    {
        if (entities.Count <= 1 || orderBy.Fields.Count == 0)
        {
            return entities;
        }

        var type = typeof(T);
        var comparer = new SafeObjectComparer(culture);
        IOrderedEnumerable<T>? ordered = null;

        foreach (var field in orderBy.Fields)
        {
            var prop = type.GetProperty(field.PropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
            {
                continue;
            }

            Func<T, object?> selector = e => prop.GetValue(e);

            // SH-M071: decided by whether a key exists yet, not by position — an unknown first field used
            // to send the second one to ThenBy on nothing.
            if (ordered == null)
            {
                ordered = field.Descending
                    ? entities.OrderByDescending(selector, comparer)
                    : entities.OrderBy(selector, comparer);
            }
            else
            {
                ordered = field.Descending
                    ? ordered.ThenByDescending(selector, comparer)
                    : ordered.ThenBy(selector, comparer);
            }
        }

        return ordered?.ToList() ?? entities;
    }

    /// <summary>
    /// A defensive object comparer for the in-memory OrderBy path (CR-L138): the default comparer throws
    /// <see cref="InvalidOperationException"/> at sort time if a value is not <see cref="IComparable"/> or
    /// two values are different types. This handles nulls (sort first), compares same-typed
    /// <see cref="IComparable"/> values directly, and falls back to a stable ordinal string comparison for
    /// everything else — so a sort on an unexpected property type degrades gracefully instead of throwing.
    /// Two strings are collated with the given culture when there is one (SH-M080).
    /// </summary>
    private sealed class SafeObjectComparer : IComparer<object?>
    {
        private readonly CultureInfo? _culture;

        public SafeObjectComparer(CultureInfo? culture)
        {
            _culture = culture;
        }

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (_culture != null && x is string xs && y is string ys)
            {
                return _culture.CompareInfo.Compare(xs, ys);
            }

            if (x is IComparable comparable && x.GetType() == y.GetType())
            {
                return comparable.CompareTo(y);
            }

            return string.CompareOrdinal(x.ToString(), y.ToString());
        }
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
