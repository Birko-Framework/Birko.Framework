using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Repositories;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.TimescaleDB.Stores;
using Birko.Configuration;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Data.TimescaleDB.Tests;

/// <summary>
/// CR-L232: a null settings on either TimescaleDBConnector constructor used to NRE deep inside
/// (AsTimescaleSettings dereferenced settings.Location) — both now throw a clear ArgumentNullException.
/// CR-L233: the copy-pasted 'Connector == null' guards across the store and both async repositories
/// were consolidated into per-class RequireConnector() helpers with one unified message — these tests
/// pin that an unconfigured instance still fails fast with the clear InvalidOperationException.
/// </summary>
public class TimescaleDBGuardTests
{
    [Fact]
    public void Connector_NullTypedSettings_ThrowsArgumentNull()
    {
        var act = () => new TimescaleDBConnector((TimescaleDBSettings)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Connector_NullRemoteSettings_ThrowsArgumentNull_NotNre()
    {
        var act = () => new TimescaleDBConnector((RemoteSettings)null!);
        act.Should().Throw<ArgumentNullException>("the RemoteSettings path used to NRE on settings.Location")
            .WithParameterName("settings");
    }

    [Fact]
    public async Task AsyncStore_WithoutSettings_SchemaMethodsFailFast()
    {
        var store = new AsyncTimescaleDBStore<TestModel>();

        (await store.Awaiting(s => s.CreateSchemaAsync()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
        (await store.Awaiting(s => s.CreateHypertableAsync("timestamp")).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
        (await store.Awaiting(s => s.DropAsync()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
    }

    [Fact]
    public async Task AsyncModelRepository_WithoutSettings_SchemaMethodsFailFast()
    {
        var repo = new AsyncTimescaleDBModelRepository<TestModel>();

        (await repo.Awaiting(r => r.InitAsync()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
        (await repo.Awaiting(r => r.CreateSchemaAsync()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
        (await repo.Awaiting(r => r.CreateHypertableAsync("timestamp")).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
        (await repo.Awaiting(r => r.DropAsync()).Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*Call SetSettings*");
    }

    [Fact]
    public async Task ModelRepository_DestroyAsync_IsNotOverridden_AndUnconfiguredDoesNotThrow()
    {
        // CR-L234 (same-defect extra): the model repository's DestroyAsync override
        // (base.DestroyAsync + DropAsync) dropped the table a second time via the unwrapped
        // connector; removed — destruction flows only through the store.
        typeof(AsyncTimescaleDBModelRepository<TestModel>).GetMethod("DestroyAsync")!
            .DeclaringType.Should().NotBe(typeof(AsyncTimescaleDBModelRepository<TestModel>));

        var repo = new AsyncTimescaleDBModelRepository<TestModel>();
        await repo.Awaiting(r => r.DestroyAsync()).Should().NotThrowAsync(
            "pre-fix the trailing DropAsync threw InvalidOperationException on an unconfigured repo");
    }

    // Note (CR-L233, verify-first): the audit's AsyncTimescaleDBRepository.cs pointer referred to a
    // never-compiled, bit-rotted copy (absent from the .projitems, no longer implementing the base's
    // abstract MapToModel). Both dead copies were DELETED — the maintained
    // TimescaleDBRepository/AsyncTimescaleDBRepository live in Birko.Data.TimescaleDB.ViewModel.
    // No reflection pin here: the legitimate ViewModel classes share the same name + namespace, so an
    // assembly-composition assert would false-fail as soon as a consumer compiles both projects together.
}
