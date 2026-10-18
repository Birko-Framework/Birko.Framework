using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Tests.TestResources.Models;
using FluentAssertions;
using Xunit;
using PasswordSettings = Birko.Configuration.PasswordSettings;

namespace Birko.Data.SQL.Tests.Views
{
    /// <summary>
    /// TASK-666: a view built while another build of the same view had already cached its fields took a field by SOURCE
    /// column name from that cache, so <c>COUNT(Orders.Guid) AS OrderCount</c> became <c>Orders.Guid AS CustomerId</c>
    /// (the first field named <c>Guid</c>). Seen on CI as a ViewDdlTests failure after ViewCacheConcurrencyTests' 64
    /// concurrent first loads. Each type here is private to its test, so the process-wide caches start empty (rule 61).
    /// </summary>
    public class Task666ViewRebuildTests
    {
        private static readonly PasswordSettings TestSettings = new() { Location = "test.db", Name = "test" };

        [View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId), connect: ViewConnect.CheckExisting)]
        public class RebuiltView
        {
            [ViewField(typeof(CustomerModel), nameof(CustomerModel.Guid))]
            public Guid? CustomerId { get; set; }

            [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
            public int OrderCount { get; set; }
        }

        [View(typeof(CustomerModel), typeof(OrderModel), nameof(CustomerModel.Guid), nameof(OrderModel.CustomerId), connect: ViewConnect.CheckExisting)]
        public class ConcurrentView
        {
            [ViewField(typeof(CustomerModel), nameof(CustomerModel.Guid))]
            public Guid? CustomerId { get; set; }

            [CountField(typeof(OrderModel), nameof(OrderModel.Guid))]
            public int OrderCount { get; set; }
        }

        private static string Sql(Tables.View view) => new ViewDdlTests.TestViewConnector(TestSettings).TestBuildViewSelectSql(view);

        [Fact]
        public void A_view_built_while_its_fields_are_already_cached_keeps_its_aggregate()
        {
            SQL.DataBase.LoadView(typeof(RebuiltView));
            // What a concurrent first load sees: the field cache filled by the build that finished, no view cached yet.
            var viewCache = (ConcurrentDictionary<Type, Tables.View>)typeof(SQL.DataBase)
                .GetField("_viewCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            viewCache.TryRemove(typeof(RebuiltView), out _);

            var sql = Sql(SQL.DataBase.LoadView(typeof(RebuiltView)));

            sql.Should().Contain("COUNT(").And.Contain("AS OrderCount");
            sql.Should().NotContain("\"Orders\".Guid AS CustomerId");
        }

        [Fact]
        public void Concurrent_first_loads_all_build_the_same_aggregate()
        {
            var views = new Tables.View[64];
            Parallel.For(0, views.Length, i => views[i] = SQL.DataBase.LoadView(typeof(ConcurrentView)));

            views.Select(Sql).Should().OnlyContain(sql => sql.Contains("COUNT(") && sql.Contains("AS OrderCount"));
        }
    }
}
