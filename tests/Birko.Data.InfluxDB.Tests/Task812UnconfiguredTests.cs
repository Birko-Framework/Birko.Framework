using System;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// TASK-812. Measured before the fix: an InfluxDB store whose settings were never set returned <c>Guid.Empty</c> from
/// <c>Create</c>, nothing from <c>Update</c> / <c>Delete</c>, and null, empty or 0 from reads. Every operation refuses now.
/// </summary>
public class Task812UnconfiguredTests
{
    [Fact]
    public async Task An_unconfigured_async_store_refuses_reads_and_writes()
    {
        var store = new AsyncInfluxDBStore<TestModel>();

        await FluentActions.Awaiting(() => store.CreateAsync(new TestModel())).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.ReadAsync(Guid.NewGuid())).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.CountAsync()).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => store.DeleteAsync(new TestModel { Guid = Guid.NewGuid() })).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void An_unconfigured_sync_store_refuses_reads_and_writes()
    {
        var store = new InfluxDBStore<TestModel>();

        FluentActions.Invoking(() => store.Create(new TestModel())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Read(Guid.NewGuid())).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => store.Count()).Should().Throw<InvalidOperationException>();
    }
}
