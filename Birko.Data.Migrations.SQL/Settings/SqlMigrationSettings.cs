using Birko.Data.SQL.Stores;

namespace Birko.Data.Migrations.SQL.Settings
{
    /// <summary>
    /// Settings for SQL migration runners.
    /// Extends SqlSettings to inherit connection timeout and command timeout configuration.
    /// </summary>
    public class SqlMigrationSettings : SqlSettings
    {
        /// <summary>
        /// Gets or sets the name of the migrations table.
        /// Default is "__Migrations".
        /// </summary>
        public string MigrationsTable { get; set; } = "__Migrations";

        /// <summary>
        /// Gets or sets the schema for the migrations table.
        /// Default is null (uses default schema).
        /// </summary>
        public string? Schema { get; set; }

        /// <summary>
        /// Gets or sets whether to use transactions during migrations.
        /// Default is true. Safe on single-writer SQLite: the runner records applied versions on its
        /// own connection/transaction (not a second connection), so there is no lock contention that
        /// would otherwise require turning this off.
        /// </summary>
        public bool UseTransaction { get; set; } = true;

        /// <summary>
        /// Gets or sets the transaction timeout in seconds.
        /// Default is 30 seconds.
        /// </summary>
        public int TransactionTimeout { get; set; } = 30;

        /// <summary>
        /// The migrations table's <b>identity</b> — <c>Schema.Table</c> when a schema is set, otherwise
        /// <c>Table</c> — with no quoting applied.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>TASK-332 — identity here, rendering on the connector.</b> This used to be
        /// <c>FullTableName</c>, which quoted each part with an ANSI double quote hardcoded in a
        /// <c>protected virtual QuoteIdentifier</c> on this class. That made a settings object a second
        /// quoting producer beside <c>AbstractConnectorBase.QuoteIdentifier</c>, and the two disagreed on
        /// half the supported providers: MySQL accepts <c>"</c> as an identifier delimiter only under
        /// <c>ANSI_QUOTES</c>, so the migrations table's own <c>CREATE TABLE</c> was rejected outright
        /// (measured on 8.4.11: <c>ERROR 1064 … near '"__Migrations_X" ("Version" BIGINT PRIMAR'</c>) —
        /// before a single model table was reached.
        /// </para>
        /// <para>
        /// A settings object cannot answer the rendering question, because it holds no connector and
        /// quoting is a provider capability — the position TASK-262 records as <i>identity on the table,
        /// rendering and capability on the connector</i>. So this property carries only the name, and
        /// <c>SqlMigrationStore</c> renders it through <c>AbstractConnectorBase.QualifiedIdentifier</c>,
        /// which quotes each dot-separated part with that provider's own delimiters. The rename from
        /// <c>FullTableName</c> is deliberate: an external reader of the old property would otherwise have
        /// silently received unquoted SQL where it used to receive quoted, and a compile error is the loud
        /// direction (§ TASK-260). Measured blast radius before renaming: 0 readers outside this project.
        /// </para>
        /// <para>
        /// A name that genuinely contains a dot stays addressable by quoting it in
        /// <see cref="MigrationsTable"/> — <c>QualifiedIdentifier</c> splits on <i>unquoted</i> dots only.
        /// </para>
        /// </remarks>
        public string QualifiedTableName
            => string.IsNullOrEmpty(Schema) ? MigrationsTable : $"{Schema}.{MigrationsTable}";
    }
}
