using Birko.Data.Localization.Models;
using Birko.Data.Localization.SQL.Mappings;
using Birko.Data.Models;
using Birko.Models.SQL.Mapping;

namespace Birko.Data.Localization.SQL;

/// <summary>
/// Registers the translation table's SQL schema. Call <c>ApplyToDatabase()</c> on the registry afterwards, before the
/// first store use, as every <c>Birko.Models.SQL</c> mapping requires.
/// </summary>
public static class LocalizationSqlMappings
{
    /// <summary>The default model, <see cref="EntityTranslationModel"/>, in <paramref name="table"/>.</summary>
    public static ModelMapRegistry Register(
        ModelMapRegistry registry, string table = EntityTranslationMapping<EntityTranslationModel>.DefaultTable)
        => Register<EntityTranslationModel>(registry, table);

    /// <summary>
    /// A consumer's own translation model — one deriving from its tenant base, for instance — in
    /// <paramref name="table"/>, with the same unique key. The table is required: two models defaulting to one table
    /// would share it with different column sets.
    /// </summary>
    public static ModelMapRegistry Register<TTranslation>(ModelMapRegistry registry, string table)
        where TTranslation : AbstractModel, IEntityTranslation
    {
        registry.Register(new EntityTranslationMapping<TTranslation>(table));
        return registry;
    }
}
