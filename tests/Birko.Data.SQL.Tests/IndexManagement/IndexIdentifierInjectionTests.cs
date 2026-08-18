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
    /// metadata or, where no entity type is available, to pass a **bare-identifier check** — the same
    /// `\A…\z`-anchored test `ValidateRuleFieldIdentifier` applies to rule fields, anchored that way because
    /// .NET's `$` also matches before a trailing newline. `SqlIndexManager` has only a table name, so it
    /// takes the check.
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
        /// The guard must not break the legitimate case — a plain column name, and the qualified form the
        /// shared identifier check already accepts.
        /// </summary>
        [Theory]
        [InlineData("Status")]
        [InlineData("TenantGuid")]
        [InlineData("_internal")]
        [InlineData("Col1")]
        [InlineData("Docs.Status")]
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
    }
}
