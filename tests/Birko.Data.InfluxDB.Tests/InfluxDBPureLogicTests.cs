using System;
using System.Net.Http;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.InfluxDB.UnitOfWork;
using Birko.Data.Patterns.UnitOfWork;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// CR-M095: the store had only no-client smoke tests. These cover the pure-logic surfaces the finding
/// called out and that are testable without a live InfluxDB server: transient-exception classification,
/// the Flux interval parser, ModelToPoint field emission, and the UnitOfWork state machine.
/// </summary>
public class InfluxDBPureLogicTests
{
    private static Settings NewSettings() => new("http://localhost:8086", "bucket", "token", "org");

    #region IsTransientException

    [Fact]
    public void IsTransientException_true_for_transient_faults()
    {
        var s = NewSettings();
        s.IsTransientException(new TimeoutException()).Should().BeTrue();
        s.IsTransientException(new HttpRequestException()).Should().BeTrue();
        s.IsTransientException(new TaskCanceledException("x", new TimeoutException())).Should().BeTrue();
        s.IsTransientException(new Exception("HTTP 429 too many requests")).Should().BeTrue();
        s.IsTransientException(new Exception("503 service unavailable")).Should().BeTrue();
    }

    [Fact]
    public void IsTransientException_false_for_non_transient_faults()
    {
        var s = NewSettings();
        s.IsTransientException(new ArgumentException("bad")).Should().BeFalse();
        s.IsTransientException(new InvalidOperationException("nope")).Should().BeFalse();
    }

    #endregion

    #region FormatFluxInterval

    [Theory]
    [InlineData("01:00:00", "1h")]
    [InlineData("00:30:00", "30m")]
    [InlineData("00:00:45", "45s")]
    [InlineData("2.00:00:00", "2d")]
    [InlineData("3 hours", "3h")]
    [InlineData("10 minutes", "10m")]
    [InlineData("5 seconds", "5s")]
    [InlineData("2 days", "2d")]
    [InlineData("garbage", "1h")]
    public void FormatFluxInterval_maps_to_flux_units(string input, string expected)
    {
        AsyncInfluxDBStore<TestModel>.FormatFluxInterval(input).Should().Be(expected);
    }

    #endregion

    #region ModelToPoint

    private sealed class PointExposingStore : AsyncInfluxDBStore<TestModel>
    {
        public string LineProtocol(TestModel m) => ModelToPoint(m).ToLineProtocol();
    }

    [Fact]
    public void ModelToPoint_emits_measurement_guid_tag_and_fields()
    {
        var store = new PointExposingStore();
        var guid = Guid.NewGuid();

        var line = store.LineProtocol(new TestModel { Guid = guid, Name = "alpha", Value = 1.5 });

        line.Should().StartWith("TestModel,", "measurement is the type name");
        line.Should().Contain($"Guid={guid}");
        line.Should().Contain("Name=\"alpha\"");
        line.Should().Contain("Value=1.5");
    }

    #endregion

    #region UnitOfWork state machine

    private static (AsyncInfluxDBStore<TestModel> store, InfluxDbUnitOfWork uow) NewUoW()
    {
        var store = new AsyncInfluxDBStore<TestModel>();
        store.SetSettings(NewSettings());
        return (store, InfluxDbUnitOfWork.FromStore(store));
    }

    [Fact]
    public void Settings_accessor_exposes_the_configured_settings()
    {
        // CR-L123: FromStore reads Bucket/Organization via this public accessor instead of reflecting the
        // private _settings field. Configuring the store must surface those values through Settings.
        var store = new AsyncInfluxDBStore<TestModel>();
        store.Settings.Should().BeNull("no settings applied yet");

        store.SetSettings(NewSettings());

        store.Settings.Should().NotBeNull();
        store.Settings!.Bucket.Should().Be("bucket");
        store.Settings.Organization.Should().Be("org");

        // FromStore succeeds off the accessor (no reflection) and yields a usable UoW.
        var uow = InfluxDbUnitOfWork.FromStore(store);
        uow.Should().NotBeNull();
        store.Dispose();
    }

    [Fact]
    public async Task Begin_activates_and_Rollback_clears_without_io()
    {
        var (store, uow) = NewUoW();

        await uow.BeginAsync();
        uow.IsActive.Should().BeTrue();

        await uow.RollbackAsync();
        uow.IsActive.Should().BeFalse();

        await uow.DisposeAsync();
        store.Dispose();
    }

    [Fact]
    public async Task Begin_twice_throws_TransactionAlreadyActive()
    {
        var (store, uow) = NewUoW();
        await uow.BeginAsync();

        Func<Task> act = () => uow.BeginAsync();

        await act.Should().ThrowAsync<TransactionAlreadyActiveException>();
        await uow.DisposeAsync();
        store.Dispose();
    }

    [Fact]
    public async Task Commit_or_Rollback_without_active_transaction_throws()
    {
        var (store, uow) = NewUoW();

        await uow.Invoking(u => u.CommitAsync()).Should().ThrowAsync<NoActiveTransactionException>();
        await uow.Invoking(u => u.RollbackAsync()).Should().ThrowAsync<NoActiveTransactionException>();

        await uow.DisposeAsync();
        store.Dispose();
    }

    [Fact]
    public async Task Begin_after_Dispose_throws_ObjectDisposed()
    {
        var (store, uow) = NewUoW();
        uow.Dispose();

        await uow.Invoking(u => u.BeginAsync()).Should().ThrowAsync<ObjectDisposedException>();
        store.Dispose();
    }

    #endregion
}
