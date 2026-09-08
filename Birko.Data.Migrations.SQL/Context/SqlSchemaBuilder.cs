using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL.Connectors;

namespace Birko.Data.Migrations.SQL.Context
{
    public class SqlSchemaBuilder : ISchemaBuilder
    {
        private readonly DbConnection _connection;
        private readonly DbTransaction? _transaction;
        private readonly AbstractConnector _connector;

        /// <summary>
        /// Creates the schema builder. <paramref name="connector"/> is <b>required</b> (TASK-247).
        /// </summary>
        /// <remarks>
        /// <para>
        /// It used to be optional, and every method carried a hand-written raw-SQL fallback for the null case.
        /// Those fallbacks were deleted: they re-derived statements the provider connectors already emit, and
        /// two of them had drifted into being <b>wrong on two providers</b> —
        /// <c>CREATE INDEX IF NOT EXISTS "Col"</c> (rejected by MySQL, and PostgreSQL cannot resolve a quoted
        /// column against the folded one bare-column DDL creates) and <c>DROP INDEX IF EXISTS x ON t</c>
        /// (rejected by MySQL for the <c>IF EXISTS</c>, invalid on PostgreSQL for the <c>ON</c>). So the
        /// "connector-free" capability was never real; it emitted broken DDL.
        /// </para>
        /// <para>
        /// It was also actively harmful: <c>connector == null</c> is how every test in this project used to
        /// build it, so six tests exercised only the dead branch — which is exactly why TASK-246's missing
        /// <c>Unique</c> flag on the <i>live</i> branch stayed green. Requiring the connector makes that class
        /// of mistake impossible rather than documented.
        /// </para>
        /// <para>
        /// Verified reachable-by-nobody before removing: the only production construction is
        /// <c>SqlMigrationRunner</c> → <c>SqlMigrationContext</c>, which requires a non-null connector, and a
        /// sweep of all 16 consumer repos found 0 hand-built contexts.
        /// <b>TASK-259 corrected the other half of that claim:</b> the same sweep, re-run, finds
        /// <c>ISchemaBuilder</c> genuinely used by <c>Symbio.Tests.Unit/MigrationRuntimeTests</c>
        /// (<c>context.Schema.CreateCollection(…).Build()</c>). No <i>production</i> consumer code uses it, so
        /// the conclusion above stands — but "0 uses" was too strong, and it is the kind of count this file's
        /// own history says to re-measure rather than cite.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="connection"/> or <paramref name="connector"/> is null.
        /// </exception>
        public SqlSchemaBuilder(DbConnection connection, DbTransaction? transaction, AbstractConnector connector)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _transaction = transaction;
            _connector = connector ?? throw new ArgumentNullException(nameof(connector),
                "SqlSchemaBuilder requires a connector: the provider emits its own DDL, and the raw-SQL "
              + "fallback this used to fall back to was wrong on MySQL and PostgreSQL. Pass the "
              + "AbstractConnector you built the migration runner with (SqlMigrationRunner already holds one).");
        }

        public ICollectionBuilder CreateCollection(string name)
        {
            return new SqlCollectionBuilder(name, _connection, _transaction, _connector);
        }

        public void DropCollection(string name)
        {
            using var boundary = EnterAmbientBoundary();
            _connector.DropTable(new[] { name });
        }

        public bool CollectionExists(string name)
        {
            // CR-L152: INFORMATION_SCHEMA is standard for MSSql/MySQL/PostgreSQL but does not exist in
            // SQLite (which uses sqlite_master), so an unconditional INFORMATION_SCHEMA query threw there.
            // Pick the catalog query from the connection's provider.
            var connTypeName = _connection.GetType().Name;
            var isSqlite = connTypeName.IndexOf("Sqlite", StringComparison.OrdinalIgnoreCase) >= 0;

            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = isSqlite
                ? "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @tableName"
                : "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @tableName";
            var param = command.CreateParameter();
            param.ParameterName = "@tableName";
            param.Value = name;
            command.Parameters.Add(param);
            var result = command.ExecuteScalar();
            return Convert.ToInt64(result) > 0;
        }

        public IIndexBuilder CreateIndex(string collectionName, string indexName)
        {
            return new SqlIndexBuilder(collectionName, indexName, _connection, _transaction, _connector);
        }

        public void DropIndex(string collectionName, string indexName)
        {
            // The deleted fallback emitted `DROP INDEX IF EXISTS x ON t`, which is wrong on both providers in
            // opposite directions: MySQL rejects the IF EXISTS but requires the ON, PostgreSQL accepts the
            // IF EXISTS but permits no ON. The connector's DropIndexSql is per-dialect and correct.
            using var boundary = EnterAmbientBoundary();
            var indexDef = new Birko.Data.SQL.Tables.IndexDefinition { Name = indexName };
            _connector.DropIndexes(collectionName, new[] { indexDef });
        }

        public void AddField(string collectionName, FieldDescriptor field)
        {
            using var boundary = EnterAmbientBoundary();
            _connector.AlterTableAdd(collectionName, new[] { SchemaField.For(field) });
        }

        public void DropField(string collectionName, string fieldName)
        {
            using var boundary = EnterAmbientBoundary();
            var field = SchemaField.For(new FieldDescriptor { Name = fieldName, Type = FieldType.String });
            _connector.AlterTableDrop(collectionName, new[] { field });
        }

        /// <summary>
        /// The one schema operation with <b>no</b> connector equivalent, so it stays hand-written.
        /// </summary>
        /// <remarks>
        /// TASK-247 deleted every other raw-SQL branch in this class; this one has nothing to delegate to —
        /// <c>AbstractConnector</c> exposes no rename. It at least quotes through the connector's dialect now
        /// rather than a hardcoded <c>"</c>.
        /// <para>
        /// ⚠ <b>DECIDED NOT TO FIX (TASK-252 #1), and here is the measurement.</b> <c>RENAME COLUMN</c>
        /// requires <b>MySQL 8.0+</b>, while <c>Birko.Data.SQL.MySQL/CLAUDE.md</c> declares support from
        /// <b>5.7</b> — so on the oldest declared-supported MySQL this method cannot work. It is not fixed
        /// because the fallback is not a dialect swap: measured on 8.4.11, <c>ALTER TABLE t CHANGE b b2</c>
        /// without a type is <c>ERROR 1064</c>, and only <c>CHANGE b b2 VARCHAR(50)</c> succeeds. So a 5.7
        /// path must first read the column's <b>full definition</b> from the catalogue and restate it —
        /// real work, on a method with <b>0</b> callers in the framework, its tests, and all 16 consumer
        /// repos (re-measured 2026-09-08). Restating a definition also risks silently changing a column
        /// that a rename should leave alone.
        /// </para>
        /// <para>
        /// The limit is recorded on <c>Birko.Data.SQL.MySQL/CLAUDE.md</c> beside the version claim it
        /// contradicts, so it is discoverable from the promise rather than only from here. If a caller ever
        /// appears on MySQL 5.7, the fix is a connector-level rename with a capability flag, in the family
        /// of <c>SupportsTransactionalDdl</c>.
        /// </para>
        /// </remarks>
        public void RenameField(string collectionName, string oldName, string newName)
        {
            // Execute() runs on _connection directly, so it needs no boundary; the scope is here because
            // QuoteIdentifier goes through the connector and a future connector-delegating rename would.
            using var boundary = EnterAmbientBoundary();
            Execute($"ALTER TABLE {QuoteIdentifier(collectionName)} RENAME COLUMN {QuoteIdentifier(oldName)} TO {QuoteIdentifier(newName)}");
        }

        /// <summary>
        /// Publishes this migration's connection and transaction as an <b>ambient</b> boundary for the
        /// duration of one operation, so the connector's own DDL emitters run on them. Dispose to leave.
        /// </summary>
        /// <remarks>
        /// TASK-259. This replaced <c>_connector.SetExternalTransaction(_connection, _transaction)</c>, which
        /// was called at three sites here and <b>never called again with nulls</b> — and this class was the
        /// legacy pair's last caller in the framework. Connectors are cached process-wide per
        /// (type, settings id) by <c>DataBase.GetConnector</c>, so that call left one migration's connection
        /// and transaction on the shared connector for the life of the process, and the runner disposes both
        /// on the way out. Measured on SQLite with the default <c>UseTransaction = true</c>: the next store
        /// against the same database took the stale branch for its <i>lazy schema-ensure</i>, which threw —
        /// and a store whose schema-ensure throws is left permanently uninitialised, so every later read and
        /// write on that entity threw too.
        /// <para>
        /// Both stores had already abandoned the same call for the same reason (see the remarks on
        /// <c>DataBaseStore.EnterTransactionScope</c> / <c>AsyncDataBaseStore</c>); TASK-240 replaced it with
        /// <c>AmbientSqlTransaction</c>, which is scoped to the async flow and restores exactly what was
        /// there. The schema builder was simply not migrated with them.
        /// </para>
        /// <para>
        /// <b>Returns null when there is no transaction, and that is the behaviour-preserving case, not a
        /// gap.</b> <c>AbstractConnector</c>'s legacy branch required <i>both</i>
        /// <c>ExternalConnection</c> and <c>ExternalTransaction</c> to be non-null, so a migration run with
        /// <c>UseTransaction = false</c> never routed connector commands onto the migration's connection — it
        /// used the connector's own. `AmbientSqlTransaction.Enter` refuses a null transaction, so declining to
        /// enter reproduces that exactly. Both shipped consumers run with transactions disabled deliberately
        /// (Symbio because its DDL goes through the connector's own connection, so an outer runner transaction
        /// deadlocks single-writer SQLite), which is why this defect was invisible in production.
        /// </para>
        /// </remarks>
        private IDisposable? EnterAmbientBoundary()
            => EnterAmbientBoundary(_connector, _connection, _transaction);

        /// <summary>
        /// The single producer of this boundary, shared with the two nested builders (TASK-259).
        /// </summary>
        /// <remarks>
        /// One method rather than one per class, deliberately. The first draft of this fix wrote the same
        /// three lines in <c>SqlSchemaBuilder</c>, <c>SqlCollectionBuilder</c> and <c>SqlIndexBuilder</c>, and
        /// reverting one of the three left the regression test <b>green</b> — the migration path runs through
        /// the nested collection builder, so the copy that mattered was not the one under test. That is the
        /// same shape § Conventions records as "a funnel with four overrides is not a funnel", and it makes
        /// the revert meaningless, which is worse than the duplication.
        /// </remarks>
        internal static IDisposable? EnterAmbientBoundary(
            AbstractConnector connector, DbConnection connection, DbTransaction? transaction)
            => transaction == null
                ? null
                : Birko.Data.SQL.Connectors.AmbientSqlTransaction.Enter(
                    connector.Settings.GetId(), connection, transaction);

        private void Execute(string sql)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private string QuoteIdentifier(string name) => _connector.QuoteIdentifier(name);

        // TASK-247 removed FieldTypeToSql and FormatValue with the raw-SQL fallbacks that were their
        // only callers. Column types now come from the provider's own FieldDefinition/ConvertType,
        // which is what makes them correct per dialect instead of approximately portable.

        private class SqlCollectionBuilder : ICollectionBuilder
        {
            private readonly string _name;
            private readonly DbConnection _connection;
            private readonly DbTransaction? _transaction;
            private readonly AbstractConnector _connector;
            private readonly List<FieldDescriptor> _fields = new();
            private readonly List<string> _primaryKeyFields = new();
            private bool _built;

            public SqlCollectionBuilder(string name, DbConnection connection, DbTransaction? transaction, AbstractConnector connector)
            {
                _name = name;
                _connection = connection;
                _transaction = transaction;
                _connector = connector;
            }

            public ICollectionBuilder WithField(string name, FieldType type,
                bool isPrimary = false, bool isUnique = false,
                bool isRequired = false, int? maxLength = null,
                int? precision = null, int? scale = null,
                bool isAutoIncrement = false, object? defaultValue = null)
            {
                var descriptor = new FieldDescriptor
                {
                    Name = name,
                    Type = type,
                    IsPrimary = isPrimary,
                    IsUnique = isUnique,
                    IsRequired = isRequired,
                    MaxLength = maxLength,
                    Precision = precision,
                    Scale = scale,
                    IsAutoIncrement = isAutoIncrement,
                    DefaultValue = defaultValue
                };
                _fields.Add(descriptor);
                if (isPrimary)
                    _primaryKeyFields.Add(name);
                return this;
            }

            public ICollectionBuilder WithField(FieldDescriptor field)
            {
                _fields.Add(field);
                if (field.IsPrimary)
                    _primaryKeyFields.Add(field.Name);
                return this;
            }

            // Public so it satisfies ICollectionBuilder.Build() — the terminal a migration calls to
            // actually emit the CREATE TABLE. Previously internal + never invoked (CR-C14).
            public void Build()
            {
                if (_built) return;
                _built = true;

                // TASK-247: the raw-SQL fallback here (and its FormatColumn helper) is gone with the
                // connector-optional constructor. It hardcoded ANSI double quotes and a provider-agnostic type
                // table, where the connector's FieldDefinition emits the dialect's own column DDL.
                //
                // NOTE the primary-key difference, so it reads as known rather than lost: the deleted fallback
                // emitted a composite `PRIMARY KEY (a, b)` clause from _primaryKeyFields, which
                // AbstractConnector.CreateTable does not — it renders PRIMARY KEY per column from the field's
                // IsPrimary flag. Nothing in the tree or in any consumer declares a composite primary key
                // through this builder (TASK-259 re-measured: one consumer TEST uses ISchemaBuilder, and it
                // declares a single-column primary key), so no behaviour in use is lost;
                // a composite primary key via migrations would need connector support, which is a task of its
                // own rather than a fallback nobody could reach correctly.
                using var boundary = EnterAmbientBoundary();
                // TASK-264. Two things here, both previously dropped on the way in:
                //   * IsIgnored is honoured, matching the [IgnoreField] / [NotMapped] check
                //     CreateAbstractField performs *before* its own dispatch — so "not a column" means
                //     the same thing on both paths. An ignored descriptor used to get a column anyway.
                //   * SchemaField.For, not `new SchemaField(...)`, so a declared maxLength / precision /
                //     scale reaches the connector at all. See the remarks on SchemaField.
                var fieldDefinitions = _fields
                    .Where(f => !f.IsIgnored)
                    .Select(f => _connector.FieldDefinition(SchemaField.For(f)));
                _connector.CreateTable(_name, fieldDefinitions);
            }

            /// <summary>Delegates to the one producer on the outer class (TASK-259).</summary>
            private IDisposable? EnterAmbientBoundary()
                => SqlSchemaBuilder.EnterAmbientBoundary(_connector, _connection, _transaction);

        }

        private class SqlIndexBuilder : IIndexBuilder
        {
            private readonly string _collectionName;
            private readonly string _indexName;
            private readonly DbConnection _connection;
            private readonly DbTransaction? _transaction;
            private readonly AbstractConnector _connector;
            private readonly List<(string Name, bool Descending)> _fields = new();
            private bool _unique;
            private bool _sparse;
            private bool _built;

            public SqlIndexBuilder(string collectionName, string indexName, DbConnection connection, DbTransaction? transaction, AbstractConnector connector)
            {
                _collectionName = collectionName;
                _indexName = indexName;
                _connection = connection;
                _transaction = transaction;
                _connector = connector;
            }

            public IIndexBuilder WithField(string name, bool descending = false, IndexFieldType fieldType = IndexFieldType.Standard)
            {
                // Validated HERE rather than in Build(), so a bad name fails at the declaration site and
                // covers both of Build()'s routes at once.
                //
                // TASK-249. This is the second caller-derived index-column sink, and it was missed when
                // TASK-245 made index columns be emitted BARE (required: a quoted column cannot resolve the
                // case-folded one PostgreSQL stores). `Build()`'s connector path puts this text straight into
                // Tables.IndexColumn.ColumnName and hands it to CreateIndexes -> CreateIndexSql, where
                // QuoteIdentifier had been incidentally containing it. Bare, a migration calling
                // WithField("Rank); CREATE TABLE Pwned (x INTEGER); --") emits and executes two statements —
                // the SH-H023 shape. The guard on SqlIndexManager.ToSqlIndexDefinition does not cover this
                // route: nothing here goes through that translator.
                _fields.Add((Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier(name), descending));
                return this;
            }

            public IIndexBuilder Unique()
            {
                _unique = true;
                return this;
            }

            /// <summary>
            /// Honoured as a partial unique index over the declared columns (TASK-274), using the
            /// <c>WhereNotNull</c> machinery TASK-273 built.
            /// </summary>
            /// <remarks>
            /// <para>
            /// This used to be <c>=> this</c>, so a migration asking for a sparse index got a full one — and
            /// for a UNIQUE index that is not a lost optimisation but a <b>stricter</b> constraint than
            /// declared, which rejects rows the declaration permits.
            /// </para>
            /// <para>
            /// <b>Single-column only, and the refusal for a compound index is the honest part.</b> Mongo's
            /// sparse compound index includes a document when <i>any</i> key is present; a SQL partial index
            /// over <c>a IS NOT NULL AND b IS NOT NULL</c> requires <i>all</i> of them. The two cannot both
            /// be what <c>Sparse()</c> means, and <c>IIndexBuilder</c> does not say which — so a compound
            /// declaration is refused rather than silently given one of the two readings.
            /// </para>
            /// </remarks>
            public IIndexBuilder Sparse()
            {
                _sparse = true;
                return this;
            }

            /// <summary>
            /// Refused (TASK-274): the SQL connector's index DDL models name, uniqueness, columns and the
            /// null-predicates — there is nothing a free-form property could reach.
            /// </summary>
            public IIndexBuilder WithProperty(string key, object value)
                => throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                    "SQL",
                    $"index property '{key}'",
                    "the SQL index emitter models only the name, uniqueness, column list and null-predicates",
                    "Use Raw() for provider-specific index syntax.");

            // Public so it satisfies IIndexBuilder.Build() — the terminal a migration calls to emit
            // the CREATE INDEX. Previously internal + never invoked (CR-C14).
            public void Build()
            {
                if (_built) return;
                _built = true;

                if (_fields.Count == 0)
                    throw new InvalidOperationException("Index must have at least one field.");

                {
                    using var boundary = EnterAmbientBoundary();
                    // TASK-246: `Unique = _unique` was missing, so a migration's .Unique() built a
                    // NON-unique index on every provider. IndexDefinition.Unique defaults to false and
                    // CreateIndexSql emits UNIQUE only when it is true, so the declared constraint was
                    // simply absent — a missing CONSTRAINT, not a missing optimisation, silently
                    // accepting the duplicate rows the migration was written to forbid.
                    //
                    // What hid it: the raw-SQL fallback below DOES honour _unique, and it is taken only
                    // when connector == null — which is how every test in this project used to build
                    // this. The feature worked in the path nobody uses and failed in the path everybody
                    // uses. Same lost-flag shape as SqlIndexManager.ToSqlIndexDefinition (TASK-245),
                    // which dropped the identical property one layer over.
                    var indexDef = new Birko.Data.SQL.Tables.IndexDefinition
                    {
                        Name = _indexName,
                        Unique = _unique
                    };

                    // TASK-274 — Sparse becomes a WhereNotNull predicate over the single declared column,
                    // which is exactly what "skips rows without the indexed field" means for one column.
                    // Compound is refused rather than given one of two incompatible readings (see Sparse()).
                    if (_sparse)
                    {
                        if (_fields.Count != 1)
                        {
                            throw Birko.Data.Patterns.Schema.IndexBuilderSupport.Unsupported(
                                "SQL",
                                $"a sparse COMPOSITE index ('{_indexName}', {_fields.Count} columns)",
                                "Mongo's sparse compound index includes a document when ANY key is present "
                                + "while a SQL partial index requires ALL of them, and IIndexBuilder does not "
                                + "say which Sparse() means",
                                "Declare the intent explicitly with [CompositeIndex(..., WhereNotNull = ...)] "
                                + "on the entity, or drop Sparse() for a full index.");
                        }
                        indexDef.Predicates.Add(new Birko.Data.SQL.Tables.IndexPredicate
                        {
                            ColumnName = _fields[0].Name,
                            RequireNull = false,
                        });
                    }
                    indexDef.Columns.AddRange(_fields.Select((f, i) => new Birko.Data.SQL.Tables.IndexColumn
                    {
                        ColumnName = f.Name,
                        Order = i,
                        IsDescending = f.Descending
                    }));
                    _connector.CreateIndexes(_collectionName, new[] { indexDef });
                }

                // TASK-247: the raw-SQL fallback that used to follow emitted
                //   CREATE {UNIQUE }INDEX IF NOT EXISTS "ix" ON "T" ("Col" ASC)
                // which was wrong twice over — MySQL rejects IF NOT EXISTS on CREATE INDEX (1064), and
                // PostgreSQL cannot resolve a quoted column against the folded one bare-column CREATE TABLE
                // actually stores (42703). It was the third copy of a statement the connector already emits
                // correctly per dialect, so it is gone rather than repaired.
            }
            /// <summary>Delegates to the one producer on the outer class (TASK-259).</summary>
            private IDisposable? EnterAmbientBoundary()
                => SqlSchemaBuilder.EnterAmbientBoundary(_connector, _connection, _transaction);

        }
    }
}
