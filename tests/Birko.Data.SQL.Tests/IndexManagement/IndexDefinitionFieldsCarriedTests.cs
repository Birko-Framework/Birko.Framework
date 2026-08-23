using System;
using System.Collections.Generic;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;
using TestConnector = Birko.Data.SQL.Tests.IndexManagement.TestConnector;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// TASK-274 — every field of a provider-neutral <see cref="IndexDefinition"/> is carried into the SQL
    /// index definition or refused. None is dropped in silence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ToSqlIndexDefinition</c> copied <c>Name</c>, <c>Unique</c> and the columns and discarded
    /// <c>Sparse</c>, <c>ExpireAfter</c> and <c>Properties</c>. That is the same lost-flag defect TASK-245
    /// fixed for <c>Unique</c> <b>in this very method</b>, on the three fields beside it — and dropping
    /// <c>Sparse</c> is worse than dropping a hint: for a unique index it produces a <i>stricter</i>
    /// constraint than declared.
    /// </para>
    /// <para>
    /// <c>Properties</c> matters especially because it is not decorative anywhere: the ElasticSearch index
    /// manager reads <c>NumberOfShards</c>/<c>NumberOfReplicas</c> from it and RavenDB's reads
    /// <c>Map</c>/<c>Maps</c>/<c>Reduce</c>. Ignoring it on the SQL lane would make one lane's contract look
    /// like another's.
    /// </para>
    /// </remarks>
    public class IndexDefinitionFieldsCarriedTests
    {
        private sealed class Probe : Birko.Data.SQL.IndexManagement.SqlIndexManager
        {
            public Probe(AbstractConnectorBase connector) : base(connector) { }

            public Birko.Data.SQL.Tables.IndexDefinition Convert(IndexDefinition definition)
                => ToSqlIndexDefinition(definition);
        }

        private static IndexDefinition Definition(params string[] fields)
        {
            var list = new List<IndexField>();
            foreach (var f in fields) list.Add(new IndexField { Name = f });
            return new IndexDefinition { Name = "ix_probe", Fields = list };
        }

        [Fact]
        public void Sparse_over_one_field_becomes_a_not_null_predicate()
        {
            var definition = Definition("Code");
            definition.Sparse = true;

            var sql = new Probe(new TestConnector()).Convert(definition);

            sql.Predicates.Should().ContainSingle();
            sql.Predicates[0].ColumnName.Should().Be("Code");
            sql.Predicates[0].RequireNull.Should().BeFalse("sparse means 'only rows that HAVE the value'");
        }

        [Fact]
        public void Sparse_over_several_fields_is_refused()
        {
            var definition = Definition("Code", "Branch");
            definition.Sparse = true;

            Action act = () => new Probe(new TestConnector()).Convert(definition);

            act.Should().Throw<NotSupportedException>()
                .Which.Message.Should().Contain("ANY key is present",
                    "Mongo's compound sparse rule and a SQL partial index's are different, and the contract "
                  + "does not say which Sparse means — so it is refused rather than silently given one");
        }

        [Fact]
        public void ExpireAfter_is_refused_because_sql_has_no_ttl_index()
        {
            var definition = Definition("Code");
            definition.ExpireAfter = TimeSpan.FromHours(1);

            Action act = () => new Probe(new TestConnector()).Convert(definition);

            act.Should().Throw<NotSupportedException>()
                .Which.Message.Should().Contain("scheduled delete");
        }

        [Fact]
        public void Properties_are_refused_because_nothing_on_this_lane_reads_them()
        {
            var definition = Definition("Code");
            definition.Properties = new Dictionary<string, object> { ["fillfactor"] = 70 };

            Action act = () => new Probe(new TestConnector()).Convert(definition);

            act.Should().Throw<NotSupportedException>()
                .Which.Message.Should().Contain("fillfactor");
        }

        /// <summary>
        /// The boundary: an ordinary definition converts exactly as it did before, so a task about three
        /// dropped fields cannot change the fourth.
        /// </summary>
        [Fact]
        public void An_ordinary_definition_is_unchanged()
        {
            var definition = Definition("TenantGuid", "Number");
            definition.Unique = true;

            var sql = new Probe(new TestConnector()).Convert(definition);

            sql.Name.Should().Be("ix_probe");
            sql.Unique.Should().BeTrue("TASK-245 — carried, and still carried");
            sql.Predicates.Should().BeEmpty();
            sql.Columns.Should().HaveCount(2);
        }
    }
}
