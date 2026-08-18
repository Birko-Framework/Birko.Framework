using System;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// TASK-245 emitted index **column** identifiers bare (they must be, or PostgreSQL cannot resolve the
    /// case-folded columns its bare-column `CREATE TABLE` creates). Bare interpolation is safe where the
    /// name comes from table metadata — schema-ensure resolves `[IndexedField]` / `[CompositeIndex]` columns
    /// against mapped properties — but `IIndexManager.CreateAsync` takes the field names from its
    /// **caller**, as free text, and interpolates them into `CommandText`.
    ///
    /// <para>
    /// Identifiers cannot be parameterised, so § Conventions requires such a name to be resolved against
    /// metadata or, where no entity type is available, to pass a **bare-identifier check** — anchored
    /// `\A…\z` because .NET's `$` also matches before a trailing newline. `SqlIndexManager` has only a table
    /// name, so it takes the check.
    /// </para>
    ///
    /// <para>
    /// **There are two such sinks, and the first fix only covered one** (TASK-249).
    /// `SqlIndexBuilder.WithField` in `Birko.Data.Migrations.SQL` also takes free text from its caller, and
    /// `Build()`'s connector path hands it to `CreateIndexes` without ever passing through
    /// `ToSqlIndexDefinition`. Both now route through `DataBase.ValidateIndexFieldIdentifier`, so they cannot
    /// disagree about what an acceptable column name is. The check is deliberately **unqualified-only**: a
    /// `CREATE INDEX` column list takes no `Table.` prefix on any supported provider.
    /// </para>
    /// </summary>
    public class IndexIdentifierInjectionTests
    {
        private static IndexDefinition Definition(params string[] fieldNames)
        {
            var fields = new IndexField[fieldNames.Length];
            for (int i = 0; i < fieldNames.Length; i++)
            {
                fields[i] = new IndexField { Name = fieldNames[i] };
            }
            return new IndexDefinition { Name = "ix_probe", Fields = fields };
        }

        private sealed class Probe : Birko.Data.SQL.IndexManagement.SqlIndexManager
        {
            public Probe(AbstractConnectorBase connector) : base(connector) { }

            public Birko.Data.SQL.Tables.IndexDefinition Translate(IndexDefinition definition)
                => ToSqlIndexDefinition(definition);
        }

        /// <summary>
        /// The payload that motivated the guard: with columns emitted bare and no check, this text would
        /// close the column list and append a second statement to the DDL — the same shape as SH-H023, where
        /// a rule field created a table.
        /// </summary>
        [Theory]
        [InlineData("Rank); CREATE TABLE Pwned (x INTEGER); --")]
        [InlineData("A, (SELECT 1)")]
        [InlineData("A\") ; DROP TABLE Docs; --")]
        [InlineData("A B")]
        [InlineData("")]
        [InlineData("  ")]
        [InlineData("A\nB")]
        public void A_field_name_that_is_not_a_bare_identifier_is_refused(string fieldName)
        {
            var probe = new Probe(new TestConnector());

            probe.Invoking(p => p.Translate(Definition(fieldName)))
                 .Should().Throw<ArgumentException>(
                     "index columns are interpolated bare into CommandText, so a name that is not a plain "
                   + "identifier must never reach the statement");
        }

        /// <summary>
        /// A trailing newline is the specific reason the check is anchored `\A…\z` rather than `^…$` —
        /// .NET's `$` matches *before* a final newline, so `"Rank\n"` would slip a `$`-anchored test.
        /// </summary>
        [Fact]
        public void A_trailing_newline_does_not_slip_the_anchor()
        {
            var probe = new Probe(new TestConnector());

            probe.Invoking(p => p.Translate(Definition("Rank\n")))
                 .Should().Throw<ArgumentException>();
        }

        /// <summary>
        /// The guard must not break the legitimate case — a plain, unqualified column name. (The
        /// table-qualified form is refused; see below.)
        /// </summary>
        [Theory]
        [InlineData("Status")]
        [InlineData("TenantGuid")]
        [InlineData("_internal")]
        [InlineData("Col1")]
        public void A_plain_identifier_is_accepted(string fieldName)
        {
            var probe = new Probe(new TestConnector());

            var translated = probe.Translate(Definition(fieldName));

            translated.Columns.Should().HaveCount(1);
            translated.Columns[0].ColumnName.Should().Be(fieldName);
        }

        /// <summary>
        /// And the refusal happens before any statement is built, so no partially-rendered DDL exists to be
        /// executed by a caller that swallows the exception.
        /// </summary>
        [Fact]
        public void The_hostile_name_never_reaches_the_emitted_statement()
        {
            var connector = new TestConnector();
            var probe = new Probe(connector);

            Action act = () =>
            {
                var translated = probe.Translate(Definition("Rank); CREATE TABLE Pwned (x INTEGER); --"));
                connector.CreateIndexSql("Docs", translated);
            };

            act.Should().Throw<ArgumentException>();
        }
    
        /// <summary>
        /// A <c>Table.Column</c> qualifier is refused — and this test previously asserted the opposite.
        /// </summary>
        /// <remarks>
        /// TASK-249. The first version of this guard reused <c>_bareIdentifier</c>, whose pattern allows an
        /// optional qualifier because it was written for the WHERE-clause sink. That is wrong here: a
        /// <c>CREATE INDEX</c> column list takes no qualifier on any supported provider, so
        /// <c>(Docs.Status)</c> is a syntax error rather than a resolvable column — the guard would have
        /// passed the payload's harmless cousin straight through to break the statement, and the framework
        /// invariant is that a qualifier is only ever emitted where a bare alias introduces it (TASK-211),
        /// which index DDL has none of. Sharing one regex was the right instinct; this sink needed the
        /// unqualified branch of it.
        /// </remarks>
        [Theory]
        [InlineData("Docs.Status")]
        [InlineData("dbo.Docs.Status")]
        public void A_table_qualified_name_is_refused(string fieldName)
        {
            var probe = new Probe(new TestConnector());

            probe.Invoking(p => p.Translate(Definition(fieldName)))
                 .Should().Throw<ArgumentException>()
                 .WithMessage("*unqualified*");
        }

        /// <summary>
        /// The second caller-derived sink, missed when index columns became bare (TASK-249, finding 1).
        /// <c>SqlIndexBuilder.WithField</c> takes free text from a migration and <c>Build()</c>'s connector
        /// path hands it to <c>CreateIndexes</c> verbatim — a route that never touches
        /// <c>ToSqlIndexDefinition</c>, so the guard above does not cover it.
        /// </summary>
        [Theory]
        [InlineData("Rank); CREATE TABLE Pwned (x INTEGER); --")]
        [InlineData("A, (SELECT 1)")]
        [InlineData("Docs.Status")]
        [InlineData("A B")]
        public void A_migration_index_field_that_is_not_a_bare_identifier_is_refused(string fieldName)
        {
            Action act = () => Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier(fieldName);

            act.Should().Throw<ArgumentException>(
                "SqlIndexBuilder.WithField routes through this same check, so the migrations sink and the "
              + "index-manager sink cannot disagree about what an acceptable column name is");
        }

        [Fact]
        public void A_plain_migration_index_field_is_accepted()
        {
            Birko.Data.SQL.DataBase.ValidateIndexFieldIdentifier("Status").Should().Be("Status");
        }
}
}
