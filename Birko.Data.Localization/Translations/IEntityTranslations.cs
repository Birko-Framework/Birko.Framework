using Birko.Data.Localization.Models;
using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Localization.Translations;

/// <summary>
/// Explicit, batched access to the translations of one localizable entity type (TASK-682): a given language per call
/// instead of the ambient culture the store wrappers follow, every language of one entity, lookups by a translated
/// value, and batched writes.
/// </summary>
/// <remarks>
/// <para>
/// Every method costs a fixed number of queries, independent of how many fields it touches: one translation read per
/// 500 entities for <see cref="GetAsync"/> and <see cref="LocalizeAsync"/>, one read for
/// <see cref="GetAllCulturesAsync"/>, at most four for <see cref="FindEntityAsync"/> and <see cref="ExistsAsync"/> on a
/// non-default culture (one fewer when no entity holds the value as its base value, one more per further 500 entities
/// that do), and one read plus at most three bulk calls for <see cref="SetAsync"/>.
/// </para>
/// <para>
/// The default culture's values are the entity's own base columns. No translation row holds them, so
/// <see cref="SetAsync"/> refuses that culture, the reads return translation rows only, and the lookups query the base
/// column.
/// </para>
/// </remarks>
public interface IEntityTranslations<T> where T : AbstractModel, ILocalizable
{
    /// <summary>
    /// <paramref name="culture"/>'s translations of <paramref name="ids"/>, as <c>entity → field → value</c>. An entity
    /// with no translation in that culture is absent; only declared localizable fields are returned.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, string>>> GetAsync(
        IEnumerable<Guid> ids, CultureInfo culture, CancellationToken ct = default);

    /// <summary>Every culture's translations of one entity, as <c>culture name → field → value</c>, in one read.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> GetAllCulturesAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>
    /// Upserts <paramref name="culture"/>'s translations of one entity in one batched write; a <c>null</c> value removes
    /// that field's translation, so reads fall back to the base value. No fields, no call.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty, <paramref name="culture"/> is the default culture (its values are the entity's
    /// base columns), or a field is not one of the type's declared localizable fields.
    /// </exception>
    Task SetAsync(Guid id, CultureInfo culture, IReadOnlyDictionary<string, string?> fields, CancellationToken ct = default);

    /// <summary>Removes one entity's translations in one culture, as one delete; its other cultures stay.</summary>
    /// <exception cref="ArgumentException"><paramref name="id"/> is empty, or <paramref name="culture"/> is the default culture.</exception>
    Task RemoveCultureAsync(Guid id, CultureInfo culture, CancellationToken ct = default);

    /// <summary>
    /// Removes every culture's translations of <paramref name="ids"/>, as one delete per 500 entities; an empty list
    /// makes no call. For purging what a deleted entity leaves behind.
    /// </summary>
    Task RemoveEntitiesAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// The entity whose <paramref name="field"/> reads <paramref name="value"/> in <paramref name="culture"/>, or
    /// <c>null</c>. An entity with a translation for that field matches only on the translation; one without matches on
    /// its base value, so an untranslated entity is still found (the wrappers' fallback, TASK-681).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not a declared localizable field.</exception>
    /// <exception cref="InvalidOperationException">More than one entity matches; the message names two of them.</exception>
    Task<Guid?> FindEntityAsync(string field, CultureInfo culture, string value, CancellationToken ct = default);

    /// <summary>
    /// Whether an entity other than <paramref name="exceptEntityGuid"/> reads <paramref name="value"/> in
    /// <paramref name="culture"/>, by the same rule as <see cref="FindEntityAsync"/> — so a value an untranslated
    /// entity holds on its base column is taken. For uniqueness checks, such as a slug free in one language.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="field"/> is not a declared localizable field.</exception>
    Task<bool> ExistsAsync(
        string field, CultureInfo culture, string value, Guid? exceptEntityGuid = null, CancellationToken ct = default);

    /// <summary>
    /// <paramref name="entities"/> as <paramref name="culture"/> shows them: detached copies carrying their translations,
    /// or their base values where a field has none, in the input order. The default culture returns them unchanged.
    /// </summary>
    Task<IReadOnlyList<T>> LocalizeAsync(IEnumerable<T> entities, CultureInfo culture, CancellationToken ct = default);
}
