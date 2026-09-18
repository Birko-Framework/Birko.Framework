using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.Stores;

namespace Birko.Data.Localization.Expressions;

/// <summary>
/// Helpers for the localized bulk-store wrappers' filter-based <c>Update(filter, PropertyUpdate)</c>
/// overrides (CR-L135). A native PropertyUpdate mutates the base column directly; when it targets a
/// localizable field on a non-default culture the wrapper must instead persist a translation, so it
/// converts the update into an <c>Action&lt;T&gt;</c> and routes through the read-modify-write path.
/// </summary>
internal static class LocalizedPropertyUpdateHelper
{
    /// <summary>
    /// Returns true if any assignment in <paramref name="updates"/> targets one of
    /// <paramref name="localizableFields"/>.
    /// </summary>
    public static bool TouchesLocalizableField<T>(PropertyUpdate<T> updates, IReadOnlyList<string> localizableFields)
        where T : Data.Models.AbstractModel
    {
        if (updates.Assignments.Count == 0 || localizableFields.Count == 0)
        {
            return false;
        }

        var fieldSet = new HashSet<string>(localizableFields);
        foreach (var (property, _) in updates.Assignments)
        {
            var name = GetMemberName(property);
            if (name != null && fieldSet.Contains(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Converts the update into an <c>Action&lt;T&gt;</c> that applies its assignments, so the
    /// read-modify-write path can run + persist translations. Reuses <c>PropertyUpdate.ApplyTo</c>
    /// (the same reflection every native store falls back to) rather than re-implementing it.
    /// </summary>
    public static Action<T> ToAction<T>(PropertyUpdate<T> updates)
        where T : Data.Models.AbstractModel
        => item => updates.ApplyTo(item);

    private static MemberExpression? GetMember(LambdaExpression property)
        => property.Body is UnaryExpression unary
            ? unary.Operand as MemberExpression
            : property.Body as MemberExpression;

    private static string? GetMemberName(LambdaExpression property) => GetMember(property)?.Member.Name;
}
