using Birko.Xaml.Core.Data;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

// A remote that can be flipped "offline" (reads throw) — reuses the public FakePeople from ViewModelTests.
internal sealed class TogglableRemote : ICrudDataSource<Person>
{
    public FakePeople Inner { get; } = new();
    public bool Offline { get; set; }

    public Task<IReadOnlyList<Person>> GetAllAsync(CancellationToken ct = default)
        => Offline ? throw new InvalidOperationException("offline") : Inner.GetAllAsync(ct);
    public Task<Person?> GetAsync(Guid id, CancellationToken ct = default)
        => Offline ? throw new InvalidOperationException("offline") : Inner.GetAsync(id, ct);
    public Task<Guid> SaveAsync(Person item, CancellationToken ct = default) => Inner.SaveAsync(item, ct);
    public Task DeleteAsync(Person item, CancellationToken ct = default) => Inner.DeleteAsync(item, ct);
    public Person NewInstance() => Inner.NewInstance();
}

public class MirrorDataSourceTests
{
    private static MirrorDataSource<Person> Make(out TogglableRemote remote, out FakePeople mirror)
    {
        remote = new TogglableRemote();
        mirror = new FakePeople();
        return new MirrorDataSource<Person>(remote, mirror, p => p.Id);
    }

    [Fact]
    public async Task Online_read_passes_through_and_populates_the_mirror()
    {
        var mds = Make(out var remote, out var mirror);
        await remote.SaveAsync(new Person { Name = "Ada" });
        await remote.SaveAsync(new Person { Name = "Grace" });

        var items = await mds.GetAllAsync();

        items.Should().HaveCount(2);
        mds.Status.Should().Be(SyncStatus.Synced);
        mirror.Count.Should().Be(2, "the mirror is refreshed from the remote");
    }

    [Fact]
    public async Task Offline_read_falls_back_to_the_mirror()
    {
        var mds = Make(out var remote, out var mirror);
        await remote.SaveAsync(new Person { Name = "Ada" });
        await mds.GetAllAsync();      // seed the mirror while online

        remote.Offline = true;
        var items = await mds.GetAllAsync();

        items.Should().HaveCount(1, "served from the mirror");
        mds.Status.Should().Be(SyncStatus.Offline);
    }

    [Fact]
    public async Task Not_found_on_remote_evicts_from_the_mirror()
    {
        var mds = Make(out var remote, out var mirror);
        var ghost = new Person { Id = Guid.NewGuid(), Name = "Ghost" };
        await mirror.SaveAsync(ghost);   // present locally, absent on the remote

        var result = await mds.GetAsync(ghost.Id);

        result.Should().BeNull();
        mds.Status.Should().Be(SyncStatus.Synced);
        (await mirror.GetAsync(ghost.Id)).Should().BeNull("a remote 404 evicts the stale mirror entry");
    }

    [Fact]
    public async Task GetAsync_online_upserts_into_the_mirror()
    {
        var mds = Make(out var remote, out var mirror);
        var id = await remote.SaveAsync(new Person { Name = "Linus" });

        var got = await mds.GetAsync(id);

        got!.Name.Should().Be("Linus");
        (await mirror.GetAsync(id)).Should().NotBeNull("a successful remote read populates the mirror");
    }
}
