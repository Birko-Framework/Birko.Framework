using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.JSON.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.JSON.Tests;

/// <summary>
/// TASK-939: a store opened on existing data whose FIRST operation is a write must not lose that data.
/// </summary>
/// <remarks>
/// The JSON twin of <c>Birko.Data.XML.Tests.Task939WriteBeforeReadTests</c>. The defect was in the async XML separate and
/// batch stores, which did not load from disk before their first write; every JSON store kind already did (all 30 cases
/// passed before the XML fix). Kept so a JSON override that forgets the load fails too.
/// </remarks>
public class Task939WriteBeforeReadTests : IDisposable
{
    private readonly string _location = "birko-json-939-" + Guid.NewGuid().ToString("N");

    public Task939WriteBeforeReadTests() => Directory.CreateDirectory(Path.GetFullPath(_location));

    public void Dispose() => TestTempDirectory.Delete(Path.GetFullPath(_location));

    public static IEnumerable<object[]> Kinds => new[]
    {
        "Store", "SeparateStore", "BatchStore", "SeparateBulkStore", "BatchBulkStore",
        "AsyncStore", "AsyncSeparateStore", "AsyncBatchStore", "AsyncSeparateBulkStore", "AsyncBatchBulkStore",
    }.Select(k => new object[] { k });

    private Settings SettingsFor(string kind) => kind.Contains("Batch")
        ? new BatchSettings { Location = _location, Name = "w" + kind, BatchSize = 2 }
        : new Settings(_location, "w" + kind);

    private static dynamic New(string kind) => kind switch
    {
        "Store" => new JsonStore<TestModel>(),
        "SeparateStore" => new JsonSeparateStore<TestModel>(),
        "BatchStore" => new JsonBatchStore<TestModel>(),
        "SeparateBulkStore" => new JsonSeparateBulkStore<TestModel>(),
        "BatchBulkStore" => new JsonBatchBulkStore<TestModel>(),
        "AsyncStore" => new AsyncJsonStore<TestModel>(),
        "AsyncSeparateStore" => new AsyncJsonSeparateStore<TestModel>(),
        "AsyncBatchStore" => new AsyncJsonBatchStore<TestModel>(),
        "AsyncSeparateBulkStore" => new AsyncJsonSeparateBulkStore<TestModel>(),
        "AsyncBatchBulkStore" => new AsyncJsonBatchBulkStore<TestModel>(),
        _ => throw new ArgumentException(kind),
    };

    private async Task<(Settings settings, List<TestModel> seeded)> Seed(string kind)
    {
        var settings = SettingsFor(kind);
        var seed = New(kind);
        seed.SetSettings(settings);
        var seeded = Enumerable.Range(0, 5).Select(i => new TestModel { Name = $"e{i}" }).ToList();
        foreach (var m in seeded)
        {
            if (kind.StartsWith("Async")) await seed.CreateAsync(m);
            else seed.Create(m);
        }
        return (settings, seeded);
    }

    private async Task<List<string>> NamesOnDisk(string kind, Settings settings)
    {
        var reader = New(kind);
        reader.SetSettings(settings);
        var all = new List<string>();
        foreach (var name in new[] { "e0", "e1", "e2", "e3", "e4", "new", "changed" })
        {
            long n = kind.StartsWith("Async")
                ? await reader.CountAsync((System.Linq.Expressions.Expression<Func<TestModel, bool>>)(x => x.Name == name))
                : reader.Count((System.Linq.Expressions.Expression<Func<TestModel, bool>>)(x => x.Name == name));
            all.AddRange(Enumerable.Repeat(name, (int)n));
        }
        return all;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_fresh_store_whose_first_operation_is_a_create_keeps_the_existing_data(string kind)
    {
        var (settings, _) = await Seed(kind);

        var fresh = New(kind);
        fresh.SetSettings(settings);
        var added = new TestModel { Name = "new" };
        if (kind.StartsWith("Async")) await fresh.CreateAsync(added);
        else fresh.Create(added);

        (await NamesOnDisk(kind, settings)).Should().BeEquivalentTo(new[] { "e0", "e1", "e2", "e3", "e4", "new" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_fresh_store_whose_first_operation_is_an_update_keeps_the_existing_data(string kind)
    {
        var (settings, seeded) = await Seed(kind);

        var fresh = New(kind);
        fresh.SetSettings(settings);
        var changed = new TestModel { Guid = seeded[3].Guid, Name = "changed" };
        if (kind.StartsWith("Async")) await fresh.UpdateAsync(changed);
        else fresh.Update(changed);

        (await NamesOnDisk(kind, settings)).Should().BeEquivalentTo(new[] { "e0", "e1", "e2", "changed", "e4" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task A_fresh_store_whose_first_operation_is_a_delete_keeps_the_rest(string kind)
    {
        var (settings, seeded) = await Seed(kind);

        var fresh = New(kind);
        fresh.SetSettings(settings);
        var gone = new TestModel { Guid = seeded[1].Guid, Name = "e1" };
        if (kind.StartsWith("Async")) await fresh.DeleteAsync(gone);
        else fresh.Delete(gone);

        (await NamesOnDisk(kind, settings)).Should().BeEquivalentTo(new[] { "e0", "e2", "e3", "e4" });
    }
}
