using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.XML.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.XML.Tests;

/// <summary>
/// TASK-939: a store opened on existing data whose FIRST operation is a write must not lose that data.
/// </summary>
/// <remarks>
/// <c>AsyncXmlSeparateStore.InitCoreAsync</c> never loaded from disk (AsyncXmlStore's does), and the write overrides of it and
/// of <c>AsyncXmlBatchStore</c> work on the in-memory items, so a fresh instance whose first call was a write saw an empty
/// store. Measured on main: a batch create left 1 of 6 entities (it rewrote batch 0 and deleted every other batch file);
/// an update threw <c>EntityNotFoundException</c> for an entity that exists; a delete silently did nothing. All four
/// async separate / batch kinds were affected; the other six XML kinds passed. Every kind is covered, so a new override
/// that forgets the load fails here.
/// </remarks>
public class Task939WriteBeforeReadTests : IDisposable
{
    private readonly string _location = "birko-xml-939-" + Guid.NewGuid().ToString("N");

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
        "Store" => new XmlStore<TestModel>(),
        "SeparateStore" => new XmlSeparateStore<TestModel>(),
        "BatchStore" => new XmlBatchStore<TestModel>(),
        "SeparateBulkStore" => new XmlSeparateBulkStore<TestModel>(),
        "BatchBulkStore" => new XmlBatchBulkStore<TestModel>(),
        "AsyncStore" => new AsyncXmlStore<TestModel>(),
        "AsyncSeparateStore" => new AsyncXmlSeparateStore<TestModel>(),
        "AsyncBatchStore" => new AsyncXmlBatchStore<TestModel>(),
        "AsyncSeparateBulkStore" => new AsyncXmlSeparateBulkStore<TestModel>(),
        "AsyncBatchBulkStore" => new AsyncXmlBatchBulkStore<TestModel>(),
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
