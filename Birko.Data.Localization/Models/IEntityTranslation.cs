using System;

namespace Birko.Data.Localization.Models;

/// <summary>
/// One translated value: the <see cref="Value"/> of <see cref="FieldName"/> on entity <see cref="EntityGuid"/> of type
/// <see cref="EntityType"/>, in <see cref="Culture"/>. The four-part key <c>(EntityGuid, EntityType, FieldName,
/// Culture)</c> identifies a row.
/// </summary>
/// <remarks>
/// TASK-680: the localization wrappers work with any model implementing this, so a consumer can store translations as
/// its own entity — one deriving from its tenant base, for instance — which a required base class would not allow.
/// <see cref="EntityTranslationModel"/> is the default implementation. A SQL store declares the key with
/// <c>Birko.Data.Localization.SQL</c>.
/// </remarks>
public interface IEntityTranslation
{
    Guid EntityGuid { get; set; }

    string EntityType { get; set; }

    string FieldName { get; set; }

    string Culture { get; set; }

    string Value { get; set; }

    DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// <see cref="LocalizedValueHash.Of"/> of <see cref="Value"/> — what an exact-value lookup selects rows by (TASK-681).
    /// The wrappers write it on every save; a model can also compute it from <see cref="Value"/> and ignore the setter,
    /// as <see cref="EntityTranslationModel"/> does. A row stored without it (seeded or imported around the wrappers by a
    /// model that does not compute it, or written before the column existed) is still found, by a slower read of the
    /// field's unhashed rows — backfill the column to keep lookups cheap.
    /// </summary>
    string ValueHash { get; set; }
}
