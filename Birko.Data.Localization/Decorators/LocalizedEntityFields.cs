using System;
using System.Collections.Generic;
using System.Reflection;

namespace Birko.Data.Localization.Decorators;

/// <summary>
/// The one producer for the two entity-level rules every localization decorator depends on:
/// how a localizable field's value is captured and restored, and how an entity is detached from
/// the instance an inner store handed back.
/// </summary>
/// <remarks>
/// <para>
/// The four wrappers (<see cref="LocalizedStoreWrapper{TStore,T}"/>,
/// <see cref="LocalizedBulkStoreWrapper{TStore,T}"/> and their async twins) each carry their own copy
/// of <c>ApplyTranslations</c> / <c>SaveTranslations</c> / <c>RewriteFilter</c>. That duplication is
/// why <c>SH-H015</c>, <c>SH-H016</c>, <c>SH-H017</c> and <c>SH-H018</c> were all present in every one
/// of them, and why a fix applied to the file a finding happened to name would have left three copies
/// live. The rules below are therefore stated once and called from all four.
/// </para>
/// <para>
/// <b>The invariant these serve (SH-H015 / SH-H017):</b> a localizable field's <i>base column</i> holds
/// the <b>default-culture</b> value, and every other culture lives in a translation row. Under a
/// non-default culture a write therefore has to persist the caller's value as a translation while
/// leaving the base column carrying whatever the default culture had — which means capturing the
/// stored base value and putting it back before the inner store sees the entity.
/// </para>
/// </remarks>
internal static class LocalizedEntityFields
{
    /// <summary>
    /// <see cref="object.MemberwiseClone"/>, reached by reflection because it is <c>protected</c>.
    /// </summary>
    /// <remarks>
    /// It is used rather than a hand-rolled property copy on purpose: a reflection copy over public
    /// writable properties silently drops anything with no public setter, which would hand a caller a
    /// partially-populated entity — a quieter defect than the one it replaces (CLAUDE.md § SH-H037,
    /// "a mapper that cannot express something refuses; it never drops it quietly"). A memberwise clone
    /// copies every field, public and private, so it cannot lose data. The framework offers no
    /// alternative: <c>AbstractModel.CopyTo(null)</c> returns <c>this</c>, and an un-overridden
    /// <c>CopyTo(new T())</c> copies only <c>Guid</c>.
    /// </remarks>
    private static readonly MethodInfo? MemberwiseCloneMethod =
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// Returns a faithful shallow copy of <paramref name="entity"/>, so a decorator can rewrite its
    /// localizable fields without writing to an object the inner store still owns (SH-H016).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why any copy at all.</b> <c>Birko.Data.InMemory</c>'s <c>ReadCore</c> returns the instance
    /// held in its dictionary, and a caching decorator returns the cached reference. Applying a
    /// translation with <c>prop.SetValue</c> to such an instance replaces the <i>store's own</i>
    /// default-culture value: one read under a non-default culture, and a later default-culture read
    /// comes back translated. Nothing in <c>IStore&lt;T&gt;</c> promises a detached read, so the
    /// decorator cannot assume one and must not write to what it did not create.
    /// </para>
    /// <para>
    /// <b>What it deliberately does not do.</b> The copy is shallow, so a mutable reference-typed
    /// member is shared with the original — but that is exactly what the caller received before this
    /// existed, so the copy is strictly an improvement and never a regression. It is safe for the
    /// decorator's own writes because those only ever target <see cref="string"/> properties.
    /// </para>
    /// </remarks>
    public static T Detach<T>(T entity)
        where T : class
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        // Defensive, not witnessed: Object.MemberwiseClone is guaranteed by the BCL, so this branch is
        // unreachable on any supported runtime. It throws rather than returning the live instance
        // because the quiet alternative is the silent store corruption this method exists to remove.
        if (MemberwiseCloneMethod == null)
        {
            throw new InvalidOperationException(
                "Object.MemberwiseClone could not be resolved, so a localized read cannot be detached "
                + "from the instance the inner store returned. Returning the live instance would let "
                + "the applied translation overwrite the store's default-culture values (SH-H016).");
        }

        return (T)MemberwiseCloneMethod.Invoke(entity, null)!;
    }

    /// <summary>
    /// Reads the current value of every localizable <see cref="string"/> property named in
    /// <paramref name="fields"/>. A field that is absent, not a string, or unreadable is skipped.
    /// </summary>
    /// <remarks>
    /// A <c>null</c> value is captured as <c>null</c> rather than omitted, so
    /// <see cref="Restore{T}"/> restores "the base column was empty" as faithfully as it restores a
    /// value.
    /// </remarks>
    public static Dictionary<string, string?> Capture<T>(T entity, IReadOnlyList<string> fields)
        where T : class
    {
        var captured = new Dictionary<string, string?>();
        if (entity == null || fields == null)
        {
            return captured;
        }

        var type = entity.GetType();
        foreach (var fieldName in fields)
        {
            var prop = ResolveLocalizableProperty(type, fieldName);
            if (prop == null || !prop.CanRead)
            {
                continue;
            }

            captured[fieldName] = prop.GetValue(entity) as string;
        }

        return captured;
    }

    /// <summary>
    /// Writes captured values back onto <paramref name="entity"/>. Only the properties present in
    /// <paramref name="values"/> are touched, so any non-localizable change an update action made
    /// survives untouched.
    /// </summary>
    public static void Restore<T>(T entity, IReadOnlyDictionary<string, string?> values)
        where T : class
    {
        if (entity == null || values == null)
        {
            return;
        }

        var type = entity.GetType();
        foreach (var pair in values)
        {
            var prop = ResolveLocalizableProperty(type, pair.Key);
            if (prop == null || !prop.CanWrite)
            {
                continue;
            }

            prop.SetValue(entity, pair.Value);
        }
    }

    /// <summary>
    /// Resolves a localizable field name to a writable-or-readable public <see cref="string"/>
    /// property, using the same guard the decorators' <c>ApplyTranslations</c> applies — so a name
    /// this helper accepts is exactly a name a translation can be applied to.
    /// </summary>
    private static PropertyInfo? ResolveLocalizableProperty(Type type, string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
        {
            return null;
        }

        var prop = type.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.PropertyType == typeof(string) ? prop : null;
    }
}
