using Birko.Security.Grants.Internal;
using FluentAssertions;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests;

public class LastOwnerTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _resource = Guid.NewGuid();

    [Fact]
    public async Task RevokingTheOnlyOwner_IsRefused_AndTheGrantStays()
    {
        var owner = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(owner, Draft, _resource);

        var revoke = () => _f.Service.RevokeAsync(owner, owner, Draft, _resource);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastOwner);
        (await _f.Service.CanAsync(owner, Draft, _resource, "share")).Should().BeTrue();
    }

    [Fact]
    public async Task DemotingTheOnlyOwner_IsRefused()
    {
        var owner = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(owner, Draft, _resource);

        var demote = () => _f.Service.GrantAsync(owner, owner, Draft, _resource, "viewer");

        (await demote.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastOwner);
        (await _f.AllAsync()).Should().ContainSingle().Which.Role.Should().Be("owner");
    }

    [Fact]
    public async Task OneOfTwoOwners_MayBeRevoked()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(first, Draft, _resource);
        await _f.Service.GrantAsync(first, second, Draft, _resource, "owner");

        (await _f.Service.RevokeAsync(second, first, Draft, _resource)).Should().BeTrue();
    }

    [Fact]
    public async Task TypeWideOwner_DoesNotCountAsAnOwnerOfTheResource()
    {
        var typeWide = Guid.NewGuid();
        var owner = Guid.NewGuid();
        await _f.SeedAsync(typeWide, Draft, null, "owner");
        await _f.Service.GrantOwnerOnCreateAsync(owner, Draft, _resource);

        var revoke = () => _f.Service.RevokeAsync(typeWide, owner, Draft, _resource);

        (await revoke.Should().ThrowAsync<GrantRefusedException>()).Which.Code.Should().Be(GrantErrorCodes.LastOwner);
    }

    [Fact]
    public async Task TwoConcurrentRevokesOfTwoOwners_LeaveExactlyOne()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(first, Draft, _resource);
        await _f.Service.GrantAsync(first, second, Draft, _resource, "owner");

        _f.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        // Each owner gives up their own ownership at the same moment; one must be refused.
        var results = await Task.WhenAll(
            Attempt(() => _f.Service.RevokeAsync(first, first, Draft, _resource)),
            Attempt(() => _f.Service.RevokeAsync(second, second, Draft, _resource)));

        results.Count(r => r == null).Should().Be(1);
        results.Single(r => r != null)!.Code.Should().Be(GrantErrorCodes.LastOwner);
        (await _f.AllAsync()).Should().ContainSingle().Which.Role.Should().Be("owner");
    }

    [Fact]
    public async Task ConcurrentRevokes_ThroughSeparateServices_AreStillSerialisedByTheSharedLock()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(first, Draft, _resource);
        await _f.Service.GrantAsync(first, second, Draft, _resource, "owner");
        var a = new Services.GrantService(_f.Store, _f.Table, _f.Clock);
        var b = new Services.GrantService(_f.Store, _f.Table, _f.Clock);
        _f.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        var results = await Task.WhenAll(
            Attempt(() => a.RevokeAsync(first, first, Draft, _resource)),
            Attempt(() => b.RevokeAsync(second, second, Draft, _resource)));

        results.Count(r => r == null).Should().Be(1);
        (await _f.AllAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ConcurrentRevokes_WithNoSharedLock_NeverLeaveTheResourceOwnerless()
    {
        // Two processes: separate lock maps, so only the post-write recount stands between them and zero owners.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(first, Draft, _resource);
        await _f.Service.GrantAsync(first, second, Draft, _resource, "owner");
        var a = new Services.GrantService(_f.Store, _f.Table, _f.Clock, null, new KeyedLock());
        var b = new Services.GrantService(_f.Store, _f.Table, _f.Clock, null, new KeyedLock());
        _f.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        var results = await Task.WhenAll(
            Attempt(() => a.RevokeAsync(first, first, Draft, _resource)),
            Attempt(() => b.RevokeAsync(second, second, Draft, _resource)));

        results.Should().Contain(r => r != null && r.Code == GrantErrorCodes.LastOwner);
        (await _f.AllAsync()).Should().Contain(g => g.Role == "owner" && g.ResourceId == _resource);
    }

    [Fact]
    public async Task ConcurrentDemotions_WithNoSharedLock_NeverLeaveTheResourceOwnerless()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _f.Service.GrantOwnerOnCreateAsync(first, Draft, _resource);
        await _f.Service.GrantAsync(first, second, Draft, _resource, "owner");
        var a = new Services.GrantService(_f.Store, _f.Table, _f.Clock, null, new KeyedLock());
        var b = new Services.GrantService(_f.Store, _f.Table, _f.Clock, null, new KeyedLock());
        _f.Store.CountDelay = TimeSpan.FromMilliseconds(50);

        var results = await Task.WhenAll(
            Attempt(() => a.GrantAsync(first, first, Draft, _resource, "viewer")),
            Attempt(() => b.GrantAsync(second, second, Draft, _resource, "viewer")));

        results.Should().Contain(r => r != null && r.Code == GrantErrorCodes.LastOwner);
        var owners = (await _f.AllAsync()).Where(g => g.Role == "owner").ToList();
        owners.Should().NotBeEmpty();
        // The restored owner keeps who granted it, not the refused caller.
        owners.Should().OnlyContain(g => (g.UserGuid == first && g.GrantedBy == null) || (g.UserGuid == second && g.GrantedBy == first));
    }

    [Fact]
    public async Task KeyedLock_ACancelledWaiter_LeavesNoEntryBehind()
    {
        var locks = new KeyedLock();
        var held = await locks.AcquireAsync("k");
        using var cts = new CancellationTokenSource();

        var waiter = locks.AcquireAsync("k", cts.Token);
        cts.Cancel();

        await ((Func<Task>)(() => waiter)).Should().ThrowAsync<OperationCanceledException>();
        held.Dispose();
        locks.Count.Should().Be(0);
    }

    [Fact]
    public async Task KeyedLock_SerialisesTwoAcquirersOfOneKey()
    {
        var locks = new KeyedLock();
        var first = await locks.AcquireAsync("k");

        var second = locks.AcquireAsync("k");
        await Task.Delay(20);
        second.IsCompleted.Should().BeFalse();

        first.Dispose();
        (await second).Dispose();
        locks.Count.Should().Be(0);
    }

    [Fact]
    public async Task KeyedLock_RemovesItsEntry_OnTheLastRelease()
    {
        var locks = new KeyedLock();

        using (await locks.AcquireAsync("k"))
            locks.Count.Should().Be(1);

        locks.Count.Should().Be(0);
    }

    private static async Task<GrantRefusedException?> Attempt(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (GrantRefusedException ex)
        {
            return ex;
        }
    }
}
