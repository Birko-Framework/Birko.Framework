using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using InfluxDB.Client.Core.Flux.Domain;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// TASK-720, without a live InfluxDB (the store authenticates by token only, and no live store fixture exists). A local
/// HTTP listener stands in for the query API: it records the Flux each read sends and answers a fixed annotated-CSV table.
/// Measured before the fix: <c>Read(Guid)</c> skipped the lazy-init gate and wrapped a cancellation in
/// <c>InvalidOperationException</c>; a filtered bulk read sent <c>limit</c> / <c>offset</c> to the server and filtered what
/// came back, so it paged the unfiltered rows; a sort took the first field's direction for every field; a UTC
/// <c>DateTime</c> came back as local time.
/// </summary>
public sealed class Task720InfluxReadTests : IDisposable
{
    public class T720Doc : AbstractModel
    {
        public string Name { get; set; } = string.Empty;
        public double Rank { get; set; }
        public DateTime When { get; set; }
    }

    /// <summary>Answers every query with the given rows and records the Flux it was sent.</summary>
    private sealed class FakeQueryApi : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        public List<string> Queries { get; } = new();
        public string Url { get; }
        public (Guid Guid, string Name, double Rank)[] Rows { get; set; } = Array.Empty<(Guid, string, double)>();

        public FakeQueryApi()
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
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }
                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                try { Queries.Add(JsonDocument.Parse(body).RootElement.GetProperty("query").GetString() ?? ""); } catch { Queries.Add(body); }

                var csv = new StringBuilder();
                csv.Append("#datatype,string,long,dateTime:RFC3339,string,string,string,double\r\n");
                csv.Append("#group,false,false,false,false,false,false,false\r\n");
                csv.Append("#default,_result,,,,,,\r\n");
                csv.Append(",result,table,_time,_measurement,Guid,Name,Rank\r\n");
                foreach (var row in Rows)
                    csv.Append($",,0,2026-01-01T00:00:00Z,T720Doc,{row.Guid},{row.Name},{row.Rank.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n");
                csv.Append("\r\n");
                var bytes = Encoding.UTF8.GetBytes(csv.ToString());
                context.Response.ContentType = "text/csv; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            _listener.Close();
        }
    }

    private readonly FakeQueryApi _server = new();

    private sealed class Counting : AsyncInfluxDBStore<T720Doc>
    {
        public int Inits;
        protected override Task InitCoreAsync(CancellationToken ct = default) { Inits++; return Task.CompletedTask; }
        public T720Doc? Map(FluxRecord record) => MapRecordToModel(record);
    }

    private sealed class SyncCounting : InfluxDBStore<T720Doc>
    {
        public int Inits;
        protected override void InitCore() { Inits++; }
    }

    private Settings Settings() => new(_server.Url.TrimEnd('/'), "bucket", "token", "org");

    private (Guid, string, double)[] ThreeRows() => new[]
    {
        (Guid.NewGuid(), "a", 1.0), (Guid.NewGuid(), "b", 2.0), (Guid.NewGuid(), "c", 2.0),
    };

    [Fact]
    public async Task ReadAsync_by_guid_runs_the_lazy_init()
    {
        var store = new Counting();
        store.SetSettings(Settings());

        await store.ReadAsync(Guid.NewGuid());

        store.Inits.Should().Be(1);
    }

    [Fact]
    public void Read_by_guid_runs_the_lazy_init()
    {
        var store = new SyncCounting();
        store.SetSettings(Settings());

        store.Read(Guid.NewGuid());

        store.Inits.Should().Be(1);
    }

    [Fact]
    public async Task ReadAsync_by_guid_with_a_cancelled_token_is_a_cancellation()
    {
        var store = new Counting();
        store.SetSettings(Settings());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await FluentActions.Awaiting(() => store.ReadAsync(Guid.NewGuid(), cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_filtered_read_pages_the_filtered_rows()
    {
        _server.Rows = ThreeRows();
        var store = new Counting();
        store.SetSettings(Settings());

        var page = (await store.ReadAsync(x => x.Name != "a", null, limit: 1, offset: 1)).ToList();

        page.Select(x => x.Name).Should().Equal("c");
        _server.Queries.Last().Should().NotContain("limit(", "the filter runs here, so the page has to be taken after it");
    }

    [Fact]
    public async Task A_sort_honours_each_field_s_direction()
    {
        _server.Rows = ThreeRows();
        var store = new Counting();
        store.SetSettings(Settings());
        var order = OrderBy<T720Doc>.ByDescending(x => x.Rank).ThenBy(x => x.Name);

        var rows = (await store.ReadAsync(null, order)).ToList();

        rows.Select(x => x.Name).Should().Equal("b", "c", "a");
    }

    [Fact]
    public void A_utc_date_comes_back_as_utc()
    {
        var record = new FluxRecord(0);
        record.Values["Guid"] = Guid.NewGuid().ToString();
        record.Values["When"] = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc).ToString("o");

        var model = new Counting().Map(record)!;

        model.When.Kind.Should().Be(DateTimeKind.Utc);
        model.When.Should().Be(new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc));
    }

    public void Dispose() => _server.Dispose();
}
