using System;
using System.Collections.Generic;
using System.Reflection;
using Birko.Data.Localization.Models;

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
    /// <paramref name="entity"/>'s <see cref="ILocalizable.GetLocalizableFields"/>, after checking that every name is a
    /// public instance <see cref="string"/> property with a public getter and setter.
    /// </summary>
    /// <remarks>
    /// SH-L079 (TASK-194): a name that was not such a property — a typo, a renamed property, a <c>decimal</c> — was
    /// skipped by every read and write, so localization of that field silently did nothing while filters still
    /// treated it as localized. It is refused instead (rule 41).
    /// </remarks>
    /// <exception cref="InvalidOperationException">A declared name is not a public read/write string property.</exception>
    public static IReadOnlyList<string> LocalizableFieldsOf(ILocalizable entity)
    {
        if (entity == null)
        {
            throw new ArgumentNullException(nameof(entity));
        }

        var type = entity.GetType();
        var fields = entity.GetLocalizableFields();
        foreach (var fieldName in fields)
        {
            LocalizableProperty(type, fieldName);
        }

        return fields;
    }

    /// <summary>
    /// Validates the declaration of every entity a write is about to persist — called <b>before</b> the inner
    /// store's write, so a refused declaration never leaves a committed row behind a failed call.
    /// </summary>
    public static void RequireValidDeclarations<T>(IEnumerable<T> entities) where T : ILocalizable
    {
        foreach (var entity in entities)
        {
            LocalizableFieldsOf(entity);
        }
    }

    /// <summary>
    /// Reads the current value of every localizable property named in <paramref name="fields"/>.
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
            captured[fieldName] = LocalizableProperty(type, fieldName).GetValue(entity) as string;
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
            LocalizableProperty(type, pair.Key).SetValue(entity, pair.Value);
        }
    }

    /// <summary>
    /// The one definition of a localizable property: a public instance <see cref="string"/> property with a public
    /// getter and setter, resolved on the most-derived type that declares the name (a property hidden with
    /// <c>new</c> resolves to the hiding one rather than being ambiguous).
    /// </summary>
    /// <exception cref="InvalidOperationException">The name is not such a property.</exception>
    private static PropertyInfo LocalizableProperty(Type type, string fieldName)
    {
        PropertyInfo? prop = null;
        for (var current = type; prop == null && current != null && !string.IsNullOrEmpty(fieldName); current = current.BaseType)
        {
            prop = current.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        }

        if (prop == null
            || prop.PropertyType != typeof(string)
            || prop.GetMethod?.IsPublic != true
            || prop.SetMethod?.IsPublic != true)
        {
            throw new InvalidOperationException(
                $"{type.Name}.GetLocalizableFields() names '{fieldName}', which is not a public read/write string "
                + "property; a localizable field must be one.");
        }

        return prop;
    }
}
