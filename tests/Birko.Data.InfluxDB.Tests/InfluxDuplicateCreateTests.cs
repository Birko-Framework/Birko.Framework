using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// Decided 2026-11-04 (the user, after TASK-720 recorded it): InfluxDB ACCEPTS a create of a Guid that is already stored,
/// as TimescaleDB does (TASK-567), and writes it as a newer point of that entity — the shape an update has. Refusing would
/// need a read before every write, across the bucket's whole history, and still could not be atomic: InfluxDB has no unique
/// constraint and no transaction, so two concurrent creates would both pass the check. This pins the acceptance, so a
/// change either way is noticed. A local listener stands in for the write API (the store authenticates by token only).
/// </summary>
public sealed class InfluxDuplicateCreateTests : IDisposable
{
    public class Reading : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeWriteApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        public List<string> Lines { get; } = new();
        public string Url { get; }

        public FakeWriteApi()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://localhost:{port}/";
            _listener.Prefixes.Add(Url);
            _listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (true)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                if (context.Request.Url!.AbsolutePath.EndsWith("/api/v2/write"))
                {
                    using var reader = new System.IO.StreamReader(context.Request.InputStream);
                    var body = await reader.ReadToEndAsync();
                    lock (Lines) Lines.Add(body);
                }
                context.Response.StatusCode = 204;
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            try { _listener.Stop(); } catch { }
            _listener.Close();
        }
    }

    private readonly FakeWriteApi _server = new();

    /// <summary>A store whose bucket-ensure is skipped, so only the write reaches the stand-in.</summary>
    private sealed class Store : AsyncInfluxDBStore<Reading>
    {
        protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task A_create_of_a_stored_guid_is_accepted_and_written_as_a_newer_point()
    {
        var store = new Store();
        store.SetSettings(new Settings(_server.Url.TrimEnd('/'), "bucket", "token", "org"));
        var guid = Guid.NewGuid();

        (await store.CreateAsync(new Reading { Guid = guid, Name = "first" })).Should().Be(guid);
        var second = () => store.CreateAsync(new Reading { Guid = guid, Name = "second" });

        await second.Should().NotThrowAsync("InfluxDB accepts a duplicate create, as TimescaleDB does");
        _server.Lines.Should().HaveCount(2);
        _server.Lines.Should().OnlyContain(l => l.Contains($"Guid={guid}"));
        _server.Lines.Last().Should().Contain("Name=\"second\"");
    }

    public void Dispose() => _server.Dispose();
}
