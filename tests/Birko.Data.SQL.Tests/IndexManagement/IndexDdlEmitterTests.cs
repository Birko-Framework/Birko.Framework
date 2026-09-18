using System;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// TASK-245 — offline pins for the index-DDL emitter. These run in CI with no database, which is the
    /// point: every end-to-end assertion for this fix lives in a gated live suite, so without these a run
    /// with no <c>BIRKO_MYSQL_HOST</c> / <c>BIRKO_PG_HOST</c> is green and proves nothing about the emitted
    /// statement.
    /// </summary>
    public class IndexDdlEmitterTests
    {
        private static Birko.Data.SQL.Tables.IndexDefinition Index(string name, bool unique, params string[] columns)
        {
            var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = name, Unique = unique };
            for (int i = 0; i < columns.Length; i++)
            {
                index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = columns[i], Order = i });
            }
            return index;
        }

        /// <summary>
        /// <b>Column identifiers bare, table identifier quoted.</b> This is the fix for PostgreSQL, where
        /// <c>CreateTable</c> emits column definitions bare so they are stored case-folded and a quoted
        /// <c>"Status"</c> cannot resolve them (measured: <c>ERROR 42703</c>). Do not "restore" the quoting
        /// from symmetry with the table name — CLAUDE.md § Conventions, and the base-table DDL is what
        /// settles it.
        /// </summary>
        [Fact]
        public void CreateIndexSql_emits_columns_bare_and_the_table_quoted()
        {
            var sql = new TestConnector().CreateIndexSql("DocNumbers", Index("ix_status", false, "Status", "Number"));

            sql.Should().Be("CREATE INDEX IF NOT EXISTS \"ix_status\" ON \"DocNumbers\" (Status, Number)");
            sql.Should().NotContain("\"Status\"", "a quoted column cannot resolve a folded one on PostgreSQL");
        }

        [Fact]
        public void CreateIndexSql_emits_unique_and_preserves_column_order()
        {
            new TestConnector().CreateIndexSql("DocNumbers", Index("ux_docnum", true, "TenantGuid", "Number"))
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_docnum\" ON \"DocNumbers\" (TenantGuid, Number)");
        }

        [Fact]
        public void CreateIndexSql_emits_desc_for_a_descending_column()
        {
            var index = Index("ix_desc", false, "Seen");
            index.Columns[0].IsDescending = true;

            new TestConnector().CreateIndexSql("Docs", index)
                .Should().Be("CREATE INDEX IF NOT EXISTS \"ix_desc\" ON \"Docs\" (Seen DESC)");
        }

        /// <summary>
        /// The base <b>keeps</b> emitting <c>IF NOT EXISTS</c> — it is only MySQL that cannot. Pinned so that
        /// nobody reads the MySQL override and "unifies" the providers from symmetry.
        /// </summary>
        [Fact]
        public void CreateIndexSql_is_conditional_by_default()
        {
            new TestConnector().CreateIndexSql("Docs", Index("ix_a", false, "A"))
                .Should().Contain("IF NOT EXISTS");
        }

        /// <summary>
        /// …and drops it on request, which is what makes <c>CreateIndexes(..., throwIfExists: true)</c> mean
        /// the same thing on every provider rather than being honoured on MySQL alone — the silent-drop shape
        /// § Conventions ranks worst.
        /// </summary>
        [Fact]
        public void CreateIndexSql_drops_the_conditional_clause_when_asked()
        {
            var sql = new TestConnector().CreateIndexSql("Docs", Index("ix_a", false, "A"), conditional: false);

            sql.Should().Be("CREATE INDEX \"ix_a\" ON \"Docs\" (A)");
            sql.Should().NotContain("IF NOT EXISTS");
        }

        /// <summary>
        /// The default predicate is <c>false</c>, and that is the whole no-behaviour-change-off-MySQL claim —
        /// asserted rather than argued. SQLite and PostgreSQL emit <c>IF NOT EXISTS</c> and MSSql synthesises
        /// a guard, so on those the "already exists" condition never reaches the client.
        /// </summary>
        [Theory]
        [InlineData("Duplicate key name 'ix_a'")]
        [InlineData("anything at all")]
        [InlineData("")]
        public void IsIndexAlreadyExistsException_is_false_by_default(string message)
        {
            var connector = new TestConnector();

            connector.IsIndexAlreadyExistsException(new Exception(message)).Should().BeFalse();
            connector.IsIndexAlreadyExistsException(
                new Exception("wrapper", new Exception(message))).Should().BeFalse(
                "not even wrapped — the base classifies nothing, by design");
        }

        /// <summary>
        /// <c>SqlIndexManager.ToSqlIndexDefinition</c> dropped <c>Unique</c>, which is the only reason a
        /// parallel <c>CreateUniqueIndexSql</c> emitter existed in three classes — one of them broken on
        /// PostgreSQL. TASK-245 carries the flag across and deletes all three; this pins the flag, since the
        /// deleted overrides cannot be tested for absence directly.
        /// </summary>
        [Fact]
        public void The_index_manager_carries_unique_into_the_connector_emitter()
        {
            var connector = new TestConnector();
            var manager = new ProbeIndexManager(connector);

            var definition = new Birko.Data.Patterns.IndexManagement.IndexDefinition
            {
                Name = "ux_probe",
                Unique = true,
                Fields = new[]
                {
                    new Birko.Data.Patterns.IndexManagement.IndexField { Name = "TenantGuid" },
                    new Birko.Data.Patterns.IndexManagement.IndexField { Name = "Number" }
                }
            };

            manager.SqlFor(definition, "Docs")
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_probe\" ON \"Docs\" (TenantGuid, Number)",
                    "one producer: the manager's unique statement IS the connector's index statement with "
                  + "Unique set, which is what let the three duplicate emitters be deleted");
        }

        /// <summary>
        /// Reaches the conversion the manager performs on the way in, so the assertion above is about the
        /// real path rather than a re-implementation of it.
        /// </summary>
        private sealed class ProbeIndexManager : Birko.Data.SQL.IndexManagement.SqlIndexManager
        {
            private readonly AbstractConnectorBase _connector;

            public ProbeIndexManager(AbstractConnectorBase connector) : base(connector) => _connector = connector;

            public string SqlFor(Birko.Data.Patterns.IndexManagement.IndexDefinition definition, string scope)
            {
                var sqlIndex = ToSqlIndexDefinition(definition);
                return _connector.CreateIndexSql(scope, sqlIndex);
            }
        }
    }
}
