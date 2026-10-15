using System.Collections.Generic;

namespace Birko.Data.SQL.SchemaDrift
{
    /// <summary>
    /// How a stored column differs from what the model declares.
    /// </summary>
    public enum ColumnDriftKind
    {
        /// <summary>
        /// The model declares the column and the table does not have it. `CREATE TABLE IF NOT EXISTS`
        /// never adds a column to an existing table, so this is what a newly-added property looks like
        /// against a database created before it.
        /// </summary>
        Missing = 0,

        /// <summary>
        /// Both have the column and the stored type is not the one <c>ConvertType</c> would emit today.
        /// The case TASK-269 exists for.
        /// </summary>
        TypeMismatch = 1,

        /// <summary>
        /// The table has a column the model does not declare. Reported, never actioned — a removed
        /// property and a column another system owns look identical from here.
        /// </summary>
        Unexpected = 2,

        /// <summary>
        /// The table has the column only under a spelling that differs in case from the one the connector
        /// emits, on a provider where that makes it a different identifier — PostgreSQL, where a bare
        /// <c>SupplierVatin</c> resolves as <c>suppliervatin</c> and never reaches a column created quoted as
        /// <c>"SupplierVatin"</c>. Every query on the column fails (<c>42703</c>), so this is not cosmetic.
        /// <see cref="ColumnDrift.StoredColumn"/> carries the stored spelling. Reported, never actioned:
        /// adding the column the connector reads would orphan the values in the stored one (TASK-610).
        /// </summary>
        CaseMismatch = 3,

        /// <summary>
        /// The column allows NULL where the model declares it NOT NULL, or the reverse (TASK-620). The first admits rows
        /// the model says cannot exist; the second fails every write that leaves the value out. <see cref="ColumnDrift.Declared"/>
        /// and <see cref="ColumnDrift.Stored"/> carry the type with its nullability. Reported, never actioned.
        /// </summary>
        NullabilityMismatch = 4,

        /// <summary>
        /// The column is part of the declared key and the table does not key it, or the reverse (TASK-567). A table
        /// created before a key-less model's <c>Guid</c> became its key has none, so a second row with the same Guid is
        /// accepted. Reported, never actioned: adding a key to a table that may already hold duplicates is a migration
        /// (CHANGELOG, TASK-567), not something the framework does on its own.
        /// </summary>
        KeyMismatch = 5,
    }

    /// <summary>
    /// One column's disagreement between a model and the table behind it.
    /// </summary>
    /// <remarks>
    /// TASK-269. <see cref="Declared"/> comes from <c>AbstractConnectorBase.ConvertType</c> — the same
    /// method <c>CREATE TABLE</c> uses — so this record cannot disagree with the DDL about what the
    /// column should be, and every column-typing rule the framework has (TASK-257, TASK-264, TASK-265,
    /// TASK-266, TASK-275) is covered without being restated here.
    /// </remarks>
    public sealed class ColumnDrift
    {
        public ColumnDrift(string table, string column, ColumnDriftKind kind, string? declared, string? stored, string? storedColumn = null)
        {
            Table = table;
            Column = column;
            Kind = kind;
            Declared = declared;
            Stored = stored;
            StoredColumn = storedColumn;
        }

        /// <summary>The table as the framework names it.</summary>
        public string Table { get; }

        /// <summary>
        /// The column name — the model's spelling, except for <see cref="ColumnDriftKind.Unexpected"/>, where
        /// there is no model field and this is the stored spelling.
        /// </summary>
        public string Column { get; }

        /// <summary>
        /// The spelling the table actually holds, for <see cref="ColumnDriftKind.CaseMismatch"/>; null otherwise.
        /// </summary>
        public string? StoredColumn { get; }

        /// <summary>What kind of disagreement this is.</summary>
        public ColumnDriftKind Kind { get; }

        /// <summary>
        /// The type <c>ConvertType</c> would emit for this field today, or null for
        /// <see cref="ColumnDriftKind.Unexpected"/>, where the model has no field to ask about.
        /// </summary>
        public string? Declared { get; }

        /// <summary>
        /// The type the table actually carries, rendered into <c>ConvertType</c>'s vocabulary by the
        /// provider, or null for <see cref="ColumnDriftKind.Missing"/>.
        /// </summary>
        public string? Stored { get; }

        public override string ToString() => Kind switch
        {
            ColumnDriftKind.Missing => $"{Table}.{Column}: declared {Declared}, not present",
            ColumnDriftKind.Unexpected => $"{Table}.{Column}: present as {Stored}, not declared",
            ColumnDriftKind.NullabilityMismatch => $"{Table}.{Column}: declared {Declared}, stored {Stored}",
            ColumnDriftKind.KeyMismatch => $"{Table}.{Column}: declared {Declared}, stored {Stored}",
            ColumnDriftKind.CaseMismatch => $"{Table}.{Column}: declared {Declared}, stored only as \"{StoredColumn}\" ({Stored}), which differs in case and is not reachable",
            _ => $"{Table}.{Column}: declared {Declared}, stored {Stored}",
        };
    }

    /// <summary>How a stored index differs from one the model declares (TASK-622).</summary>
    public enum IndexDriftKind
    {
        /// <summary>Declared, absent from the table. Queries that rely on it scan, and a declared unique rule is unenforced.</summary>
        Missing = 0,

        /// <summary>Both have the index under that name, over different columns or in a different order.</summary>
        ColumnsDiffer = 1,

        /// <summary>Declared unique and stored plain, or the reverse. A plain one admits the duplicates the model forbids.</summary>
        UniquenessDiffers = 2,
    }

    /// <summary>
    /// One declared index's disagreement with the table (TASK-622). Undeclared stored indexes are never reported: a DBA's
    /// own index is not drift.
    /// </summary>
    public sealed class IndexDrift
    {
        public IndexDrift(string table, string index, IndexDriftKind kind, string declared, string? stored)
        {
            Table = table;
            Index = index;
            Kind = kind;
            Declared = declared;
            Stored = stored;
        }

        public string Table { get; }

        /// <summary>The index name as the model declares it.</summary>
        public string Index { get; }

        public IndexDriftKind Kind { get; }

        /// <summary>The declared shape, e.g. <c>UNIQUE (TenantGuid, Number)</c>.</summary>
        public string Declared { get; }

        /// <summary>The stored shape, or null for <see cref="IndexDriftKind.Missing"/>.</summary>
        public string? Stored { get; }

        public override string ToString() => Kind == IndexDriftKind.Missing
            ? $"{Table}.{Index}: declared {Declared}, not present"
            : $"{Table}.{Index}: declared {Declared}, stored {Stored}";
    }

    /// <summary>
    /// The outcome of comparing one entity's declared columns against the table behind it.
    /// </summary>
    /// <remarks>
    /// ⚠ <b><see cref="Supported"/> is not the same as "no drift", and conflating them is the whole
    /// point of having it.</b> A provider that cannot be asked, or a table that does not exist yet,
    /// must not read as a clean bill of health — that is the silence TASK-204's channel already
    /// demonstrates, where "nobody reported anything" and "nothing is wrong" were indistinguishable
    /// for the life of the framework.
    /// </remarks>
    public sealed class SchemaDriftReport
    {
        public SchemaDriftReport(string table, bool supported, bool tableExists, IReadOnlyList<ColumnDrift> drifts, string? reason = null,
            IReadOnlyList<IndexDrift>? indexDrifts = null)
        {
            Table = table;
            Supported = supported;
            TableExists = tableExists;
            Drifts = drifts;
            Reason = reason;
            IndexDrifts = indexDrifts ?? System.Array.Empty<IndexDrift>();
        }

        /// <summary>Declared indexes the table lacks or holds differently (TASK-622).</summary>
        public IReadOnlyList<IndexDrift> IndexDrifts { get; }

        public string Table { get; }

        /// <summary>
        /// False when this provider has no column catalogue this framework knows how to read. The
        /// report then carries no drifts and asserts nothing.
        /// </summary>
        public bool Supported { get; }

        /// <summary>
        /// False when the table is not there at all. Not drift: a store creates its table lazily on
        /// first use, so an untouched entity legitimately has none.
        /// </summary>
        public bool TableExists { get; }

        public IReadOnlyList<ColumnDrift> Drifts { get; }

        /// <summary>Why the report is unsupported or empty, when that needs saying.</summary>
        public string? Reason { get; }

        /// <summary>
        /// True only when the question was actually answered and the answer was "no disagreement".
        /// </summary>
        public bool IsClean => Supported && TableExists && Drifts.Count == 0 && IndexDrifts.Count == 0;
    }
}
