using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// Composite UNIQUE index support driven by [IndexedField(..., IsUnique: true)] — the storage-level
    /// backstop for per-tenant uniqueness such as (TenantGuid, Number). Covers attribute → LoadIndexes
    /// (Tables.IndexDefinition.Unique) and the connector DDL (CREATE UNIQUE INDEX), plus that ordinary
    /// non-unique indexes are unaffected.
    /// </summary>
    public class CompositeUniqueIndexTests
    {
        [Table("DocNumbers")]
        public class DocNumberModel : AbstractLogModel
        {
            [IndexedField("ux_docnum", 0, IsUnique: true)]
            public Guid TenantGuid { get; set; }

            [IndexedField("ux_docnum", 1, IsUnique: true)]
            public string Number { get; set; } = null!;

            [IndexedField("ix_status", 0)]
            public string Status { get; set; } = null!;
        }

        [Fact]
        public void LoadIndexes_CompositeIsUnique_SetsUniqueAndOrdersColumns()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(DocNumberModel));

            table.Indexes.Should().ContainKey("ux_docnum");
            var ux = table.Indexes["ux_docnum"];
            ux.Unique.Should().BeTrue();
            ux.Columns.Select(c => c.ColumnName).Should().Equal("TenantGuid", "Number");
        }

        [Fact]
        public void LoadIndexes_NonUnique_LeavesUniqueFalse()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(DocNumberModel));

            table.Indexes.Should().ContainKey("ix_status");
            table.Indexes["ix_status"].Unique.Should().BeFalse();
        }

        [Fact]
        public void CreateIndexSql_UniqueComposite_EmitsCreateUniqueIndexOverAllColumns()
        {
            var connector = new TestConnector();
            var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ux_docnum", Unique = true };
            index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "TenantGuid", Order = 0 });
            index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "Number", Order = 1 });

            var sql = connector.CreateIndexSql("DocNumbers", index);

            sql.Should().Contain("CREATE UNIQUE INDEX");
            sql.Should().Contain("TenantGuid");
            sql.Should().Contain("Number");
            // single statement over both columns, TenantGuid first — scope the ordinal check to the
            // parenthesised column list (the table name "DocNumbers" also contains "Number").
            var columnList = sql.Substring(sql.LastIndexOf('('));
            columnList.IndexOf("TenantGuid", StringComparison.Ordinal)
               .Should().BeLessThan(columnList.IndexOf("Number", StringComparison.Ordinal));
        }

        [Fact]
        public void CreateIndexSql_NonUnique_EmitsPlainCreateIndex()
        {
            var connector = new TestConnector();
            var index = new Birko.Data.SQL.Tables.IndexDefinition { Name = "ix_status" };
            index.Columns.Add(new Birko.Data.SQL.Tables.IndexColumn { ColumnName = "Status", Order = 0 });

            var sql = connector.CreateIndexSql("DocNumbers", index);

            sql.Should().Contain("CREATE INDEX");
            sql.Should().NotContain("UNIQUE");
        }

        [Fact]
        public void IndexDefinition_UniqueDefaultsFalse()
        {
            new Birko.Data.SQL.Tables.IndexDefinition().Unique.Should().BeFalse();
        }
    }
}
