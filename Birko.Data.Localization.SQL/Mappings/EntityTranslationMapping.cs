using System;
using System.Linq;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Models.SQL.Mapping;

namespace Birko.Data.Localization.SQL.Mappings;

/// <summary>
/// The SQL schema of a translation table, for any model implementing <see cref="IEntityTranslation"/>: the
/// <see cref="EntityTranslationModel"/> default or a consumer's own (TASK-680).
/// </summary>
/// <remarks>
/// <para>
/// <b>The unique key is what makes one row per key true</b> (SH-M074). Writes read the key, then create or update;
/// two writers can both read nothing and both create. With <see cref="KeyIndex"/> the store refuses the second create,
/// and <c>Birko.Data.Localization</c>'s upsert retries it as an update — one row, the last value.
/// </para>
/// <para>
/// <b>No index on <c>Value</c>.</b> An index key on MySQL and SQL Server bounds the column to 255 characters (rule 37),
/// which would cap every translation. Exact-value lookups use <see cref="ValueHashIndex"/> on <c>ValueHash</c>
/// (SHA-256 hex, 64 characters) instead, and confirm the value in memory (TASK-681).
/// </para>
/// <para>
/// <b>The key's string columns are bounded</b> (TASK-684): <see cref="MaxEntityTypeLength"/>,
/// <see cref="MaxFieldNameLength"/> and <see cref="MaxCultureLength"/>. Left at rule 37's 255, the key was 3 204 bytes
/// under MySQL's utf8mb4, over InnoDB's 3 072, and MySQL refused it (ERROR 1071). Bounded, it is 1 508 bytes on MySQL and
/// 698 on SQL Server (limit 1 700). A longer value is refused by the database (SQLite does not enforce lengths). An
/// existing table keeps its columns: schema-ensure never alters one.
/// </para>
/// </remarks>
public sealed class EntityTranslationMapping<TTranslation> : IModelMapping<TTranslation>
    where TTranslation : AbstractModel, IEntityTranslation
{
    public const string DefaultTable = "BirkoEntityTranslations";

    /// <summary><c>EntityType</c> holds a CLR type's simple name (<c>typeof(T).Name</c>); 128 is SQL Server's <c>sysname</c>.</summary>
    public const int MaxEntityTypeLength = 128;

    /// <summary><c>FieldName</c> holds a property name.</summary>
    public const int MaxFieldNameLength = 128;

    /// <summary>
    /// <c>Culture</c> holds <c>CultureInfo.Name</c> (<c>sk-SK</c>, <c>sk</c>, <c>ca-ES-valencia</c>); 85 is Windows'
    /// <c>LOCALE_NAME_MAX_LENGTH</c> (decided by the user). Every standard name is far shorter (about 15 characters at
    /// most); a custom name with extension subtags longer than 85 is refused by the database.
    /// </summary>
    public const int MaxCultureLength = 85;

    /// <param name="table">The table name; a consumer storing its own model usually names its own table.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="table"/> is not a plain identifier of at most <see cref="MaxTableLength"/> characters. The index
    /// name is built from it, so a qualified name would put a dot in it and a long one would be truncated by
    /// PostgreSQL's 63-character limit, and the index would no longer be found by its name.
    /// </exception>
    public EntityTranslationMapping(string table = DefaultTable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(table);
        if (table.Length > MaxTableLength || !(char.IsLetter(table[0]) || table[0] == '_')
            || !table.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            throw new ArgumentException(
                $"Translation table '{table}' must be a plain identifier (letters, digits, underscore) of at most "
                + $"{MaxTableLength} characters.", nameof(table));
        }
        Table = table;
    }

    /// <summary>Leaves room for the index-name affixes (<c>IX_</c> … <c>_ValueHash</c>) inside PostgreSQL's 63-character identifiers.</summary>
    public const int MaxTableLength = 50;

    public string Table { get; }

    /// <summary>The unique index over <c>(EntityGuid, EntityType, FieldName, Culture)</c>.</summary>
    public string KeyIndex => KeyIndexName(Table);

    public static string KeyIndexName(string table) => $"UX_{table}_Key";

    /// <summary>The non-unique index on <c>ValueHash</c> that exact-value lookups use (TASK-681).</summary>
    public string ValueHashIndex => ValueHashIndexName(Table);

    public static string ValueHashIndexName(string table) => $"IX_{table}_ValueHash";

    public void Configure(ModelMap<TTranslation> map)
    {
        map.ToTable(Table).HasPrimary(x => x.Guid);

        map.Property(x => x.EntityGuid).IsRequired().HasUniqueIndex(KeyIndex, 0);
        map.Property(x => x.EntityType).IsRequired().HasMaxLength(MaxEntityTypeLength).HasUniqueIndex(KeyIndex, 1);
        map.Property(x => x.FieldName).IsRequired().HasMaxLength(MaxFieldNameLength).HasUniqueIndex(KeyIndex, 2);
        map.Property(x => x.Culture).IsRequired().HasMaxLength(MaxCultureLength).HasUniqueIndex(KeyIndex, 3);
        map.Property(x => x.Value).IsRequired();
        // Not required: a column added to an existing table cannot be NOT NULL without a default. The hash alone is
        // selective, and a column belongs to one fluent index, so it is not appended to the key's columns.
        map.Property(x => x.ValueHash).HasIndex(ValueHashIndex);
    }
}
