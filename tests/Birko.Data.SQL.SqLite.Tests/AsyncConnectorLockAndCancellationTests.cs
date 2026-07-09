using System;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// CR-H082: the isLock=true async path used Task.Run(() => { lock (_lock) { return RunCommandAsync(); } }),
/// which released the monitor at the first await — providing no serialization. CR-H083: the async
/// reader path never threaded CancellationToken, so a running query could not be cancelled. These
/// tests exercise a real SQLite database.
/// </summary>
public class AsyncConnectorLockAndCancellationTests : IDisposable
{
    private readonly string _root;

    public AsyncConnectorLockAndCancellationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlite-lock-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
        }
    }

    private SqLiteConnector NewConnector()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();

        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "app.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Widget) });
        return connector;
    }

    [Fact]
    public async Task DoCommandAsync_WithLock_SerializesConcurrentCommands()
    {
        var connector = NewConnector();

        int active = 0;
        bool overlapDetected = false;

        async Task RunOne()
        {
            await connector.DoCommandAsync(
                createCommand: cmd => { cmd.CommandText = "SELECT 1"; return Task.CompletedTask; },
                executeCommand: async cmd =>
                {
                    if (Interlocked.Increment(ref active) > 1) overlapDetected = true;
                    await Task.Delay(40);
                    Interlocked.Decrement(ref active);
                },
                isLock: true);
        }

        await Task.WhenAll(RunOne(), RunOne(), RunOne());

        overlapDetected.Should().BeFalse("isLock=true must serialize the awaited DB work");
    }

    [Fact]
    public async Task SelectAsync_ObservesCancellation()
    {
        var connector = NewConnector();

        // Seed a few rows so the reader has something to stream.
        for (int i = 0; i < 5; i++)
        {
            var w = new Widget { Guid = Guid.NewGuid(), Name = "w" + i };
            await connector.InsertAsync(typeof(Widget), w);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () =>
        {
            await foreach (var _ in connector.SelectAsync(typeof(Widget), (System.Linq.Expressions.LambdaExpression?)null, null, null, null, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
