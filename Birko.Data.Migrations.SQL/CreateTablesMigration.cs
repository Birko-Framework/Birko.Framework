using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Migrations.Context;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL
{
    /// <summary>
    /// A migration that provisions a set of tables straight from their registered
    /// <c>IModelMapping&lt;T&gt;</c> definitions — <c>Up</c> creates them, <c>Down</c> drops them — by
    /// reusing the connector's mapping-driven <see cref="AbstractConnector.CreateTable(Type[])"/> /
    /// <see cref="AbstractConnector.DropTable(Type[])"/>. This keeps the model mappings as the single
    /// source of truth for the schema instead of re-declaring columns in migration code, which is
    /// exactly what a version-tracked "create the initial schema" step usually wants.
    /// </summary>
    /// <remarks>
    /// Provider-agnostic: it takes the abstract connector, not a provider-specific one. The connector
    /// is supplied to the constructor (rather than read from <see cref="IMigrationContext"/>) because
    /// the table-from-mapping path lives on the connector, not on <c>IMigrationContext.Schema</c>.
    /// <c>CreateTable</c> emits <c>CREATE TABLE IF NOT EXISTS</c>, so re-running is harmless even
    /// though the runner's version tracking already prevents it. The types must have their mappings
    /// applied to the database registry (e.g. via <c>ModelMapRegistry.ApplyToDatabase()</c>) before
    /// this runs.
    /// <para>
    /// <b>Run this with <c>SqlMigrationSettings.UseTransaction = false</c>.</b> It provisions via the
    /// connector's own connection rather than the migration context's connection/transaction, so an
    /// outer runner transaction (on a different connection) would contend with the DDL on
    /// single-writer databases like SQLite ("database is locked"). Wrapping connector-driven DDL in a
    /// runner transaction buys no atomicity anyway. (Migrations that build their schema through
    /// <c>IMigrationContext.Schema</c> / <c>.Connection</c> instead can use the default
    /// <c>UseTransaction = true</c>.)
    /// </para>
    /// </remarks>
    public class CreateTablesMigration : Data.Migrations.AbstractMigration
    {
        private readonly AbstractConnector _connector;
        private readonly Type[] _tables;

        /// <summary>
        /// Creates a mapping-driven table-provisioning migration.
        /// </summary>
        /// <param name="connector">The SQL connector whose registered mappings define the tables.</param>
        /// <param name="tables">The model types to create/drop (each must have a registered mapping).</param>
        /// <param name="version">The migration version. Defaults to 1 (the usual initial schema).</param>
        /// <param name="name">The migration name. Defaults to "CreateTables".</param>
        public CreateTablesMigration(AbstractConnector connector, IEnumerable<Type> tables, long version = 1, string name = "CreateTables")
        {
            _connector = connector ?? throw new ArgumentNullException(nameof(connector));
            if (tables is null)
            {
                throw new ArgumentNullException(nameof(tables));
            }
            _tables = tables.ToArray();
            Version = version;
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        /// <inheritdoc />
        public override long Version { get; }

        /// <inheritdoc />
        public override string Name { get; }

        /// <inheritdoc />
        public override string Description => $"Create {_tables.Length} table(s) from their registered mappings.";

        /// <summary>Creates every table from its registered mapping. Idempotent (CREATE TABLE IF NOT EXISTS).</summary>
        public override void Up(IMigrationContext context) => _connector.CreateTable(_tables);

        /// <summary>Drops every table this migration created (no data preservation).</summary>
        public override void Down(IMigrationContext context) => _connector.DropTable(_tables);
    }
}
