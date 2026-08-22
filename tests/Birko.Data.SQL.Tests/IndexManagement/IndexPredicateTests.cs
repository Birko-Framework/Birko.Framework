using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// TASK-273 — partial/filtered index predicates: <c>WhereNotNull</c> / <c>WhereNull</c> on
    /// <c>[CompositeIndex]</c> and <c>[IndexedField]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Offline half. Every behavioural assertion for this feature needs a live server, and all four provider
    /// suites are gated on <c>BIRKO_*_HOST</c> — so without these a CI run with no database is green and
    /// proves nothing about the emitted statement or about which declarations are refused.
    /// </para>
    /// <para>
    /// Why the measured facts are quoted in these tests rather than left in the task file: the whole design
    /// rests on a per-provider asymmetry (SQL Server treats NULLs as equal, the other three do not; MySQL has
    /// no partial index at all) that is invisible in the code. Measured 2026-08-22 on SQL Server 2022
    /// (16.0.4265.3), PostgreSQL 16.15, TimescaleDB 2/PG16, MySQL 8.4.11 and SQLite 3.53.3.
    /// </para>
    /// </remarks>
    public class IndexPredicateTests
    {
        #region Emitter

        private static Birko.Data.SQL.Tables.IndexDefinition Index(
            string name, bool unique, string[] columns, params (string Column, bool RequireNull)[] predicates)
        {
            var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = name, Unique = unique };
            for (int i = 0; i < columns.Length; i++)
            {
                index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = columns[i], Order = i });
            }
            foreach (var (column, requireNull) in predicates)
            {
                index.Predicates.Add(new Birko.Data.SQL.Tables.IndexPredicate { ColumnName = column, RequireNull = requireNull });
            }
            return index;
        }

        /// <summary>
        /// The motivating case: unique per tenant only over rows that HAVE an external id. Without the tail
        /// this index rejects the second row whose <c>ExternalId</c> is NULL on SQL Server (measured
        /// <c>Msg 2601</c>), which is the ordinary case rather than an edge one.
        /// </summary>
        [Fact]
        public void CreateIndexSql_appends_an_is_not_null_predicate()
        {
            new TestConnector().CreateIndexSql("Accounts",
                    Index("ux_extid", true, new[] { "TenantGuid", "ExternalId" }, ("ExternalId", false)))
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_extid\" ON \"Accounts\" (TenantGuid, ExternalId) "
                           + "WHERE ExternalId IS NOT NULL");
        }

        /// <summary>
        /// The soft-delete case, and the reason the predicate lives on the index rather than on a column:
        /// <c>DeletedAt</c> is <b>not</b> a key column. A per-column flag could not express this at all.
        /// </summary>
        [Fact]
        public void CreateIndexSql_appends_an_is_null_predicate_over_a_non_key_column()
        {
            new TestConnector().CreateIndexSql("Orders",
                    Index("ux_live_docnum", true, new[] { "TenantGuid", "Number" }, ("DeletedAt", true)))
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_live_docnum\" ON \"Orders\" (TenantGuid, Number) "
                           + "WHERE DeletedAt IS NULL");
        }

        [Fact]
        public void CreateIndexSql_joins_several_predicates_with_and_in_order()
        {
            new TestConnector().CreateIndexSql("Orders",
                    Index("ux_two", true, new[] { "TenantGuid", "Number" }, ("Number", false), ("DeletedAt", true)))
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_two\" ON \"Orders\" (TenantGuid, Number) "
                           + "WHERE Number IS NOT NULL AND DeletedAt IS NULL");
        }

        /// <summary>
        /// Predicate columns are emitted <b>bare</b>, like the key columns. On PostgreSQL a quoted
        /// <c>"ExternalId"</c> cannot resolve the folded column <c>CreateTable</c> actually created — the
        /// TASK-245 lesson, which this new sink has to inherit rather than re-decide.
        /// </summary>
        [Fact]
        public void CreateIndexSql_emits_predicate_columns_bare()
        {
            var sql = new TestConnector().CreateIndexSql("Accounts",
                Index("ux_extid", true, new[] { "TenantGuid" }, ("ExternalId", false)));

            sql.Should().Contain("WHERE ExternalId IS NOT NULL");
            sql.Should().NotContain("\"ExternalId\"");
        }

        /// <summary>
        /// Criterion 7 — an ordinary index's statement is byte-identical to what it was before this feature
        /// existed. Asserted as an exact string on the base emitter, and on both overrides in their own
        /// provider suites.
        /// </summary>
        [Fact]
        public void CreateIndexSql_is_byte_identical_without_predicates()
        {
            new TestConnector().CreateIndexSql("DocNumbers", Index("ix_status", false, new[] { "Status", "Number" }))
                .Should().Be("CREATE INDEX IF NOT EXISTS \"ix_status\" ON \"DocNumbers\" (Status, Number)");
        }

        /// <summary>
        /// The tail survives <c>conditional: false</c> — that flag is about <c>IF NOT EXISTS</c>, not about
        /// which rows the index covers. Getting this wrong would make
        /// <c>CreateIndexes(..., throwIfExists: true)</c> quietly create a DIFFERENT index.
        /// </summary>
        [Fact]
        public void CreateIndexSql_keeps_the_predicate_when_not_conditional()
        {
            new TestConnector().CreateIndexSql("Accounts",
                    Index("ux_extid", true, new[] { "TenantGuid" }, ("ExternalId", false)), conditional: false)
                .Should().Be("CREATE UNIQUE INDEX \"ux_extid\" ON \"Accounts\" (TenantGuid) WHERE ExternalId IS NOT NULL");
        }

        #endregion

        #region Capability

        /// <summary>
        /// True by default — three of the four providers support this natively. MySQL's <c>false</c> is
        /// asserted in its own suite, and asserting BOTH sides is what stops the capability being
        /// indistinguishable from an unconditional emit.
        /// </summary>
        [Fact]
        public void SupportsPartialIndexes_is_true_by_default()
        {
            new TestConnector().SupportsPartialIndexes.Should().BeTrue();
        }

        /// <summary>
        /// Where partial indexes are unsupported, an <c>IS NOT NULL</c> term is <b>dropped</b> and the
        /// statement is exactly the unfiltered one. Safe only because such a provider treats NULLs as
        /// distinct, so the unfiltered index enforces the same rule (measured on MySQL 8.4.11: two NULL rows
        /// accepted under a plain unique index).
        /// </summary>
        [Fact]
        public void A_provider_without_partial_indexes_drops_an_is_not_null_term()
        {
            var index = Index("ux_extid", true, new[] { "TenantGuid", "ExternalId" }, ("ExternalId", false));

            new NoPartialIndexConnector().CreateIndexSql("Accounts", index)
                .Should().Be("CREATE UNIQUE INDEX IF NOT EXISTS \"ux_extid\" ON \"Accounts\" (TenantGuid, ExternalId)",
                    "the unfiltered index means the same thing where NULLs are distinct");
        }

        /// <summary>
        /// …and an <c>IS NULL</c> term is <b>refused</b>, because dropping it does not preserve the meaning:
        /// measured on all four providers, a full unique index rejects a row whose duplicate is
        /// soft-deleted, so the silently-emitted index would be STRICTER than the one declared. That is the
        /// outcome the task's criterion 4 forbids.
        /// </summary>
        [Fact]
        public void A_provider_without_partial_indexes_refuses_an_is_null_term()
        {
            var index = Index("ux_live", true, new[] { "TenantGuid", "Number" }, ("DeletedAt", true));

            Action act = () => new NoPartialIndexConnector().CreateIndexSql("Orders", index);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*WhereNull*DeletedAt*")
                .Which.Message.Should().Contain("stricter",
                    "the message has to say why it cannot just be dropped, or the next reader drops it");
        }

        /// <summary>
        /// The funnel refuses before <c>DoDdlCommand</c>, so the exception is not re-wrapped by
        /// <c>InitException</c> into a bare <c>Exception</c> a caller cannot select by type.
        /// </summary>
        [Fact]
        public void The_funnel_guard_refuses_an_is_null_term_and_passes_the_rest()
        {
            var connector = new NoPartialIndexConnector();

            connector.Guard(Index("ux_a", true, new[] { "A" }))
                .Should().BeNull("an ordinary index is untouched");
            connector.Guard(Index("ux_b", true, new[] { "A" }, ("B", false)))
                .Should().BeNull("an IS NOT NULL term is droppable, so it is not refused");

            var refusal = connector.Guard(Index("ux_c", true, new[] { "A" }, ("DeletedAt", true)));
            refusal.Should().NotBeNull();
            refusal!.Message.Should().Contain("ERROR 1064", "name the provider's own error, not a paraphrase");
            refusal.Message.Should().Contain("remove the WhereNull declaration",
                "§ SH-H037 — a guard that only says no gets reached around");
        }

        /// <summary>
        /// A provider that CAN express the predicate never reaches the guard's refusal, in either polarity.
        /// </summary>
        [Fact]
        public void The_funnel_guard_is_inert_where_partial_indexes_are_supported()
        {
            var connector = new PartialIndexConnector();

            connector.Guard(Index("ux_c", true, new[] { "A" }, ("DeletedAt", true))).Should().BeNull();
            connector.Guard(Index("ux_d", true, new[] { "A" }, ("B", false))).Should().BeNull();
        }

        private class NoPartialIndexConnector : TestConnector
        {
            public override bool SupportsPartialIndexes => false;

            public Exception? Guard(Birko.Data.SQL.Tables.IndexDefinition index)
            {
                try { RequireExpressiblePredicates(index); return null; }
                catch (Exception ex) { return ex; }
            }
        }

        private class PartialIndexConnector : TestConnector
        {
            public Exception? Guard(Birko.Data.SQL.Tables.IndexDefinition index)
            {
                try { RequireExpressiblePredicates(index); return null; }
                catch (Exception ex) { return ex; }
            }
        }

        #endregion

        #region Declarations — resolved and refused at table load

        public abstract class TenantBase : AbstractLogModel
        {
            public Guid TenantGuid { get; set; }
        }

        [Table("IpAccounts")]
        [CompositeIndex("ux_ipaccount_extid", nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
            WhereNotNull = new[] { nameof(ExternalId) })]
        public class IpAccount : TenantBase
        {
            public string? ExternalId { get; set; }
        }

        [Table("IpOrders")]
        [CompositeIndex("ux_iporder_live", nameof(TenantGuid), nameof(Number), IsUnique = true,
            WhereNull = new[] { nameof(DeletedAt) })]
        public class IpOrder : TenantBase
        {
            public string? Number { get; set; }
            public DateTime? DeletedAt { get; set; }
        }

        [Table("IpRemap")]
        [CompositeIndex("ux_ipremap", nameof(TenantGuid), nameof(Code), IsUnique = true,
            WhereNotNull = new[] { nameof(Code) })]
        public class IpRemap : TenantBase
        {
            [NamedField("doc_code")]
            public string? Code { get; set; }
        }

        [Table("IpUnmapped")]
        [CompositeIndex("ux_ipunmapped", nameof(TenantGuid), IsUnique = true, WhereNotNull = new[] { "NotAProperty" })]
        public class IpUnmapped : TenantBase
        {
        }

        [Table("IpNotNull")]
        [CompositeIndex("ux_ipnotnull", nameof(TenantGuid), IsUnique = true, WhereNull = new[] { nameof(TenantGuid) })]
        public class IpNotNull : TenantBase
        {
        }

        [Table("IpPrimary")]
        [CompositeIndex("ux_ipprimary", nameof(Code), IsUnique = true, WhereNull = new[] { nameof(Code) })]
        public class IpPrimary
        {
            [PrimaryField]
            public string Code { get; set; } = null!;
        }

        [Table("IpContradiction")]
        [CompositeIndex("ux_ipcontra", nameof(TenantGuid), nameof(Number), IsUnique = true,
            WhereNotNull = new[] { nameof(Number) }, WhereNull = new[] { nameof(Number) })]
        public class IpContradiction : TenantBase
        {
            public string? Number { get; set; }
        }

        [Table("IpMerged")]
        public class IpMerged : TenantBase
        {
            [IndexedField("ux_ipmerged", 0, IsUnique: true, WhereNotNull = new[] { nameof(Number) })]
            public string? Number { get; set; }

            [IndexedField("ux_ipmerged", 1, WhereNull = new[] { nameof(DeletedAt) })]
            public string? Branch { get; set; }

            public DateTime? DeletedAt { get; set; }
        }

        [Table("IpRepeated")]
        public class IpRepeated : TenantBase
        {
            [IndexedField("ux_iprepeated", 0, IsUnique: true, WhereNotNull = new[] { nameof(Number) })]
            public string? Number { get; set; }

            [IndexedField("ux_iprepeated", 1, WhereNotNull = new[] { nameof(Number) })]
            public string? Branch { get; set; }
        }

        [Table("IpCrossContradiction")]
        public class IpCrossContradiction : TenantBase
        {
            [IndexedField("ux_ipcross", 0, IsUnique: true, WhereNotNull = new[] { nameof(Number) })]
            public string? Number { get; set; }

            [IndexedField("ux_ipcross", 1, WhereNull = new[] { nameof(Number) })]
            public string? Branch { get; set; }
        }

        [Table("IpPlain")]
        [CompositeIndex("ux_ipplain", nameof(TenantGuid), nameof(Number), IsUnique = true)]
        public class IpPlain : TenantBase
        {
            public string? Number { get; set; }
        }

        [Fact]
        public void A_composite_declaration_resolves_its_where_not_null_column()
        {
            var index = Birko.Data.SQL.DataBase.LoadTable(typeof(IpAccount)).Indexes!["ux_ipaccount_extid"];

            index.Predicates.Should().HaveCount(1);
            index.Predicates[0].ColumnName.Should().Be("ExternalId");
            index.Predicates[0].RequireNull.Should().BeFalse();
        }

        [Fact]
        public void A_composite_declaration_resolves_a_where_null_column_that_is_not_a_key()
        {
            var index = Birko.Data.SQL.DataBase.LoadTable(typeof(IpOrder)).Indexes!["ux_iporder_live"];

            index.Columns.Select(c => c.ColumnName).Should().NotContain("DeletedAt");
            index.Predicates.Should().ContainSingle();
            index.Predicates[0].ColumnName.Should().Be("DeletedAt");
            index.Predicates[0].RequireNull.Should().BeTrue();
        }

        /// <summary>
        /// A predicate names a PROPERTY and resolves to its mapped column, so <c>[NamedField]</c> and
        /// <c>ModelMap</c> remaps are honoured for free — and no caller-typed text reaches the DDL.
        /// </summary>
        [Fact]
        public void A_predicate_honours_a_named_field_remap()
        {
            var index = Birko.Data.SQL.DataBase.LoadTable(typeof(IpRemap)).Indexes!["ux_ipremap"];

            index.Predicates.Should().ContainSingle();
            index.Predicates[0].ColumnName.Should().Be("doc_code");
        }

        /// <summary>
        /// Merged across every attribute contributing to one index name, exactly as <c>IsUnique</c> is, with
        /// <c>WhereNotNull</c> terms before <c>WhereNull</c> ones. Determinism is not cosmetic: the
        /// byte-identical assertions above compare the emitted statement.
        /// </summary>
        [Fact]
        public void Predicates_from_several_indexed_fields_merge_in_a_deterministic_order()
        {
            var index = Birko.Data.SQL.DataBase.LoadTable(typeof(IpMerged)).Indexes!["ux_ipmerged"];

            index.Predicates.Select(p => (p.ColumnName, p.RequireNull))
                .Should().Equal(("Number", false), ("DeletedAt", true));
        }

        /// <summary>
        /// The same column named by two contributing attributes yields ONE term — otherwise the statement
        /// carries <c>X IS NOT NULL AND X IS NOT NULL</c>, which is valid SQL and still breaks the
        /// byte-identical comparison.
        /// </summary>
        [Fact]
        public void A_repeated_predicate_column_collapses_to_one_term()
        {
            Birko.Data.SQL.DataBase.LoadTable(typeof(IpRepeated)).Indexes!["ux_iprepeated"]
                .Predicates.Should().ContainSingle();
        }

        [Fact]
        public void An_unmapped_predicate_property_throws_at_table_load()
        {
            Action act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(IpUnmapped));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
                .WithMessage("*NotAProperty*not a mapped column*");
        }

        /// <summary>
        /// A <c>WhereNull</c> over a column this framework declares NOT NULL indexes zero rows — an
        /// attribute that cannot mean anything useful, so it refuses rather than silently doing nothing
        /// (§ SH-H037).
        /// </summary>
        [Fact]
        public void A_predicate_over_a_not_null_column_throws_at_table_load()
        {
            Action act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(IpNotNull));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
                .WithMessage("*TenantGuid*NOT NULL*");
        }

        /// <summary>
        /// <b>The IsPrimary arm, and why it exists.</b> <c>AbstractField.IsNotNull</c> is derived from the CLR
        /// type for value types, but for <c>string</c> and <c>byte[]</c> it is set only by
        /// <c>[RequiredField]</c> / <c>[Required]</c> — so a <c>string</c> primary key reads
        /// <c>IsNotNull == false</c>. Checking <c>IsNotNull</c> alone (as this task's plan originally said)
        /// would accept a <c>WhereNull</c> on a primary key and index zero rows, silently.
        /// </summary>
        [Fact]
        public void A_predicate_over_a_string_primary_key_throws_at_table_load()
        {
            Action act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(IpPrimary));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
                .WithMessage("*Code*primary key*");
        }

        [Fact]
        public void The_same_column_in_both_lists_throws_at_table_load()
        {
            Action act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(IpContradiction));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
                .WithMessage("*Number*both WhereNotNull and WhereNull*");
        }

        /// <summary>
        /// …including when the contradiction is spread across two properties, which is the case that is
        /// invisible before the merge and the reason validation runs afterwards.
        /// </summary>
        [Fact]
        public void A_contradiction_across_two_indexed_fields_throws_at_table_load()
        {
            Action act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(IpCrossContradiction));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
                .WithMessage("*Number*both WhereNotNull and WhereNull*");
        }

        /// <summary>
        /// An ordinary declaration carries no predicates at all — the other half of criterion 7, at the
        /// metadata layer rather than at the emitter.
        /// </summary>
        [Fact]
        public void A_declaration_without_predicates_has_none()
        {
            Birko.Data.SQL.DataBase.LoadTable(typeof(IpPlain)).Indexes!["ux_ipplain"]
                .Predicates.Should().BeEmpty();
        }

        #endregion
    }
}
