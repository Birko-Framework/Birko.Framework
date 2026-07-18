using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Birko.Data.SQL.Tests.IndexManagement
{
    /// <summary>
    /// Class-level [CompositeIndex] — declares a composite (optionally UNIQUE) index whose columns may be
    /// inherited from a base class. This is what per-property [IndexedField] cannot express: a discriminator
    /// (TenantGuid) on a shared base plus a number column on the derived entity.
    /// </summary>
    public class ClassLevelCompositeIndexTests
    {
        public abstract class TenantBase : AbstractLogModel
        {
            public Guid TenantGuid { get; set; }
        }

        [Table("ClProdOrders")]
        [CompositeIndex("ux_prodorder_docnum", nameof(TenantGuid), nameof(OrderNumber), IsUnique = true)]
        public class ClProdOrder : TenantBase
        {
            public string OrderNumber { get; set; } = null!;
        }

        [Table("ClRemap")]
        [CompositeIndex("ux_remap", nameof(TenantGuid), nameof(Code), IsUnique = true)]
        public class ClRemap : TenantBase
        {
            [NamedField("doc_code")]
            public string Code { get; set; } = null!;
        }

        [Table("ClMulti")]
        [CompositeIndex("ux_multi_a", nameof(TenantGuid), nameof(Number), IsUnique = true)]
        [CompositeIndex("ix_multi_b", nameof(Number), nameof(TenantGuid))]
        public class ClMulti : TenantBase
        {
            public string Number { get; set; } = null!;
        }

        [Table("ClBad")]
        [CompositeIndex("ux_bad", nameof(TenantGuid), "NotAProperty")]
        public class ClBad : TenantBase
        {
            public string Number { get; set; } = null!;
        }

        [Fact]
        public void CompositeIndex_ResolvesInheritedColumn_AndIsUnique()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(ClProdOrder));

            table.Indexes.Should().ContainKey("ux_prodorder_docnum");
            var ux = table.Indexes["ux_prodorder_docnum"];
            ux.Unique.Should().BeTrue();
            // TenantGuid is declared on the base class, OrderNumber on the derived class.
            ux.Columns.Select(c => c.ColumnName).Should().Equal("TenantGuid", "OrderNumber");
        }

        [Fact]
        public void CompositeIndex_HonoursNamedFieldColumnRemap()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(ClRemap));

            table.Indexes["ux_remap"].Columns.Select(c => c.ColumnName)
                .Should().Equal("TenantGuid", "doc_code");
        }

        [Fact]
        public void MultipleCompositeIndexes_UniqueAndNonUnique_Coexist()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(ClMulti));

            table.Indexes["ux_multi_a"].Unique.Should().BeTrue();
            table.Indexes["ux_multi_a"].Columns.Select(c => c.ColumnName).Should().Equal("TenantGuid", "Number");

            table.Indexes["ix_multi_b"].Unique.Should().BeFalse();
            table.Indexes["ix_multi_b"].Columns.Select(c => c.ColumnName).Should().Equal("Number", "TenantGuid");
        }

        [Fact]
        public void CompositeIndex_UnknownProperty_ThrowsAtTableLoad()
        {
            var act = () => Birko.Data.SQL.DataBase.LoadTable(typeof(ClBad));

            act.Should().Throw<Birko.Data.Exceptions.TableAttributeException>()
               .WithMessage("*ux_bad*NotAProperty*not a mapped column*");
        }

        [Fact]
        public void CompositeIndex_EmitsCreateUniqueIndex_ViaConnector()
        {
            var table = Birko.Data.SQL.DataBase.LoadTable(typeof(ClProdOrder));
            var sql = new TestConnector().CreateIndexSql("ClProdOrders", table.Indexes["ux_prodorder_docnum"]);

            sql.Should().Contain("CREATE UNIQUE INDEX");
            sql.Should().Contain("TenantGuid");
            sql.Should().Contain("OrderNumber");
        }
    }
}
