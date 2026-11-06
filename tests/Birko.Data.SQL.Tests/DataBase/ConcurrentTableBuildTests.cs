using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase;

/// <summary>
/// TASK-276 — concurrent first builds of one type's table publish ONE consistent table. A build writes into the cached
/// field objects every build of the type shares (<c>field.Table</c>, <c>IsPrimary</c>, <c>IsIndexed</c>), so two builds at
/// once published a table whose fields pointed at the other build's unpublished copy (measured: 3,997 of 4,000 rounds
/// with 8 threads, 0 single-threaded). Builds are now serialised.
/// </summary>
/// <remarks>
/// The rounds clear the table and field caches by reflection, which is process-wide state (rule 61): only for the two
/// types declared here, which no other class loads, so a parallel sibling cannot see the cleared entries.
/// </remarks>
public class ConcurrentTableBuildTests
{
    [Table("CtbAccounts")]
    [CompositeIndex("ux_ctbaccount_extid", nameof(TenantGuid), nameof(ExternalId), IsUnique = true,
        WhereNotNull = new[] { nameof(ExternalId) })]
    public class CtbAccount : AbstractLogModel
    {
        public Guid TenantGuid { get; set; }
        public string? ExternalId { get; set; }
    }

    [Table("CtbCodes")]
    public class CtbCode : AbstractModel
    {
        [UniqueField] public string? Code { get; set; }
        public string? Name { get; set; }
    }

    private static void ForgetCachedTable(Type type)
    {
        foreach (var name in new[] { "_tableCache", "_fieldsCache", "_unmappedTypes" })
        {
            var cache = typeof(Birko.Data.SQL.DataBase).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException($"DataBase.{name} is gone: this test needs updating, not skipping.");
            ((IDictionary)cache.GetValue(null)!).Remove(type);
        }
    }

    private static string Shape(Tables.Table t) =>
        string.Join("|", t.Fields.Values.OrderBy(f => f.Name).Select(f => $"{f.Name}:{f.IsPrimary}:{f.IsIndexed}"))
        + "#" + string.Join("|", t.Indexes.Keys.OrderBy(k => k));

    [Theory]
    [InlineData(typeof(CtbAccount))]
    [InlineData(typeof(CtbCode))]
    public void Concurrent_first_builds_publish_a_table_whose_fields_are_its_own(Type type)
    {
        var expected = Shape(Birko.Data.SQL.DataBase.LoadTable(type));
        const int threads = 8, rounds = 300;
        int foreign = 0, reshaped = 0;

        for (var round = 0; round < rounds; round++)
        {
            ForgetCachedTable(type);
            using var start = new Barrier(threads);
            Task.WaitAll(Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                Birko.Data.SQL.DataBase.LoadTable(type);
            })).ToArray());

            var table = Birko.Data.SQL.DataBase.LoadTable(type);
            if (table.Fields.Values.Any(f => !ReferenceEquals(f.Table, table))) foreign++;
            if (Shape(table) != expected) reshaped++;
        }

        foreign.Should().Be(0, "every field of the published table must point at that table, not at a losing build's copy");
        reshaped.Should().Be(0, "keys, index flags and indexes are the same however many builds raced");
    }
}
