using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.SQL.Tests;

/// <summary>
/// TASK-825 (reported by Symbio, TASK-998). <c>ApplyToDatabase</c> evicted the cached table on every call
/// (<c>RegisterTableName</c> had no "unchanged → no-op"), <c>LoadTable</c> published the rebuilt table bare, and only then
/// were the fluent indexes written onto it, into a plain dictionary. A concurrent reader got a table without the indexes,
/// and one reading <c>Indexes</c> during the write could throw. Any later eviction lost the fluent part for good.
/// </summary>
public class Task825ApplyPublishRaceTests
{
    // No [Table]: the table name comes from the fluent mapping only, as in Symbio's EntityTranslation.
    public class Translation : Birko.Data.Models.AbstractModel
    {
        public string? Key { get; set; }
        public string? Culture { get; set; }
        public string? ValueHash { get; set; }
        public string? Value { get; set; }
    }

    private sealed class TranslationMap : IModelMapping<Translation>
    {
        public void Configure(ModelMap<Translation> map)
        {
            map.ToTable("T825Translations");
            map.Property(x => x.Key).HasUniqueIndex("UX_T825_Key", 0);
            map.Property(x => x.Culture).HasUniqueIndex("UX_T825_Key", 1);
            map.Property(x => x.ValueHash).HasIndex("IX_T825_ValueHash");
        }
    }

    public class SchemaTranslation : Birko.Data.Models.AbstractModel
    {
        public string? Key { get; set; }
    }

    private sealed class SchemaTranslationMap : IModelMapping<SchemaTranslation>
    {
        public void Configure(ModelMap<SchemaTranslation> map)
        {
            map.ToTable("T825SchemaTranslations", "loc");
            map.Property(x => x.Key).HasUniqueIndex("UX_T825S_Key");
        }
    }

    private static void Apply<T>(IModelMapping<T> mapping) where T : class
    {
        var registry = new ModelMapRegistry();
        registry.Register(mapping);
        registry.ApplyToDatabase();
    }

    private static bool HasFluentIndexes(Birko.Data.SQL.Tables.Table table) =>
        table.Indexes != null && table.Indexes.ContainsKey("UX_T825_Key") && table.Indexes.ContainsKey("IX_T825_ValueHash");

    [Fact]
    public async Task A_reader_racing_a_reapply_always_sees_the_fluent_indexes()
    {
        Apply(new TranslationMap());
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
        long loads = 0, bare = 0, threw = 0;

        var writer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested) Apply(new TranslationMap());
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                Interlocked.Increment(ref loads);
                try
                {
                    var table = DataBase.LoadTable(typeof(Translation));
                    if (!HasFluentIndexes(table) || table.Indexes!.Values.ToArray().Length < 2) Interlocked.Increment(ref bare);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref threw);
                }
            }
        })).ToArray();
        await Task.WhenAll(readers.Append(writer));

        loads.Should().BeGreaterThan(0);
        bare.Should().Be(0, $"of {loads} loads, none may see the table before its fluent indexes are on it");
        threw.Should().Be(0);
    }

    [Fact]
    public void Reapplying_the_same_mappings_keeps_the_published_table()
    {
        Apply(new TranslationMap());
        var first = DataBase.LoadTable(typeof(Translation));

        Apply(new TranslationMap());

        DataBase.LoadTable(typeof(Translation)).Should().BeSameAs(first, "nothing changed, so nothing is evicted or rewritten");
    }

    [Fact]
    public void Reapplying_a_mapping_with_a_schema_keeps_the_published_table()
    {
        Apply(new SchemaTranslationMap());
        var first = DataBase.LoadTable(typeof(SchemaTranslation));

        Apply(new SchemaTranslationMap());

        var again = DataBase.LoadTable(typeof(SchemaTranslation));
        again.Should().BeSameAs(first);
        again.Schema.Should().Be("loc");
    }

    [Fact]
    public void Registering_an_unchanged_table_name_changes_nothing()
    {
        Apply(new TranslationMap());
        var first = DataBase.LoadTable(typeof(Translation));

        DataBase.RegisterTableName(typeof(Translation), "T825Translations");

        DataBase.LoadTable(typeof(Translation)).Should().BeSameAs(first);
    }

    [Fact]
    public void A_table_rebuilt_after_a_later_eviction_still_has_its_fluent_indexes()
    {
        Apply(new TranslationMap());
        var first = DataBase.LoadTable(typeof(Translation));

        // A changed registration evicts the table; the rebuild must carry the fluent part too.
        DataBase.RegisterMaxLength(typeof(Translation), nameof(Translation.Value), 777);
        DataBase.RegisterMaxLength(typeof(Translation), nameof(Translation.Value), 778);
        var rebuilt = DataBase.LoadTable(typeof(Translation));

        rebuilt.Should().NotBeSameAs(first);
        HasFluentIndexes(rebuilt).Should().BeTrue();
        rebuilt.Indexes!["UX_T825_Key"].Unique.Should().BeTrue();
    }

    public class Dropped : Birko.Data.Models.AbstractModel
    {
        public string? Code { get; set; }
    }

    private sealed class DroppedWithIndex : IModelMapping<Dropped>
    {
        public void Configure(ModelMap<Dropped> map)
        {
            map.ToTable("T825Dropped");
            map.Property(x => x.Code).HasIndex("IX_T825_Code");
        }
    }

    private sealed class DroppedNameOnly : IModelMapping<Dropped>
    {
        public void Configure(ModelMap<Dropped> map) => map.ToTable("T825Dropped");
    }

    [Fact]
    public void A_mapping_reapplied_without_properties_drops_its_earlier_fluent_part()
    {
        Apply(new DroppedWithIndex());
        DataBase.LoadTable(typeof(Dropped)).Indexes.Should().ContainKey("IX_T825_Code");

        Apply(new DroppedNameOnly());

        (DataBase.LoadTable(typeof(Dropped)).Indexes ?? new()).Should().NotContainKey("IX_T825_Code");
    }
}
