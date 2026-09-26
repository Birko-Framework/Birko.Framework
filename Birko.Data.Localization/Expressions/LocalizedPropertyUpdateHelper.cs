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
        foreach (var assignment in updates.Assignments)
        {
            var name = GetMemberName(assignment.Property);
            if (name != null && fieldSet.Contains(name))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Throws if <paramref name="updates"/> increments one of <paramref name="localizableFields"/>. An increment
    /// has no translation-row meaning, so it is refused on <b>every</b> culture — the same call must not pass or
    /// fail depending on the request's culture.
    /// </summary>
    /// <exception cref="NotSupportedException">An increment targets a localizable field.</exception>
    public static void RefuseIncrementOnLocalizableField<T>(PropertyUpdate<T> updates, IReadOnlyList<string> localizableFields)
        where T : Data.Models.AbstractModel
    {
        if (updates.Assignments.Count == 0 || localizableFields.Count == 0)
        {
            return;
        }

        var fieldSet = new HashSet<string>(localizableFields);
        foreach (var assignment in updates.Assignments)
        {
            var name = GetMemberName(assignment.Property);
            if (name == null || !fieldSet.Contains(name) || !assignment.Match(set => false, increment => true))
            {
                continue;
            }

            throw new NotSupportedException(
                $"Cannot increment '{typeof(T).Name}.{name}': it is localizable, and an increment cannot be expressed as a translation.");
        }
    }

    /// <summary>
    /// Throws if <paramref name="updates"/> carries any increment. Called on the non-default-culture path that
    /// replays the update as read-modify-save: an increment on a non-localizable field there would lose its
    /// atomicity silently, while the identical call on the default culture stays atomic.
    /// </summary>
    /// <exception cref="NotSupportedException">The update carries an increment.</exception>
    public static void RefuseIncrementOnReadModifyWriteFallback<T>(PropertyUpdate<T> updates)
        where T : Data.Models.AbstractModel
    {
        foreach (var assignment in updates.Assignments)
        {
            if (!assignment.Match(set => false, increment => true))
            {
                continue;
            }

            throw new NotSupportedException(
                $"Cannot increment '{typeof(T).Name}.{assignment.MemberPath}' in the same update as a localizable field on a "
                + "non-default culture: that update is replayed as read-modify-save, where the increment is not atomic. "
                + "Send the increment as its own update.");
        }
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
