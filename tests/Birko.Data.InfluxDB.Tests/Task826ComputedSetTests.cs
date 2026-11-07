using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InfluxDB.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InfluxDB.Tests;

/// <summary>
/// TASK-826 on InfluxDB, which has no native update: <c>Update(filter, PropertyUpdate)</c> reads the matching points,
/// applies the formula in C# (<c>PropertyUpdate.ApplyTo</c>) and writes them back. The store authenticates by token only
/// and no live store fixture exists, so a local listener stands in for the query API (answering two rows that sit on a
/// rounding midpoint) and the write API (recording the line protocol). A decimal is stored as a double here, as for
/// every InfluxDB write; the rounded value survives that exactly.
/// </summary>
public sealed class Task826ComputedSetTests : IDisposable
{
    public class Price : AbstractModel
    {
        public string Code { get; set; } = string.Empty;
        public decimal Gross { get; set; }
        public decimal Net { get; set; }
    }

    private sealed class FakeInflux : IDisposable
    {
        private readonly HttpListener _listener = new();
        public List<string> Writes { get; } = new();
        public string Url { get; }
        public (Guid Guid, string Code, decimal Gross)[] Rows { get; set; } = Array.Empty<(Guid, string, decimal)>();

        public FakeInflux()
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
                using var reader = new System.IO.StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                if (context.Request.Url!.AbsolutePath.EndsWith("/api/v2/write"))
                {
                    lock (Writes) Writes.Add(body);
                    context.Response.StatusCode = 204;
                    context.Response.Close();
                    continue;
                }

                var csv = new StringBuilder();
                csv.Append("#datatype,string,long,dateTime:RFC3339,string,string,string,double,double\r\n");
                csv.Append("#group,false,false,false,false,false,false,false,false\r\n");
                csv.Append("#default,_result,,,,,,,\r\n");
                csv.Append(",result,table,_time,_measurement,Guid,Code,Gross,Net\r\n");
                foreach (var row in Rows)
                    csv.Append($",,0,2026-01-01T00:00:00Z,Price,{row.Guid},{row.Code},{row.Gross.ToString(System.Globalization.CultureInfo.InvariantCulture)},0\r\n");
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
            try { _listener.Stop(); } catch { }
            _listener.Close();
        }
    }

    private sealed class Store : InfluxDBStore<Price>
    {
        protected override void InitCore() { }
    }

    private sealed class AsyncStore : AsyncInfluxDBStore<Price>
    {
        protected override Task InitCoreAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private readonly FakeInflux _server = new();

    private Settings Settings() => new(_server.Url.TrimEnd('/'), "bucket", "token", "org");

    private static PropertyUpdate<Price> NetFromGross(MidpointRounding mode)
        => new PropertyUpdate<Price>().Compute(x => x.Net, x => Math.Round(x.Gross / 1.2m, 2, mode));

    private string WrittenNetOf(string code)
    {
        string[] lines;
        lock (_server.Writes) lines = _server.Writes.SelectMany(w => w.Split('\n')).ToArray();
        var line = lines.Single(l => l.Contains($"Code=\"{code}\""));
        return line.Split(' ')[1].Split(',').Single(f => f.StartsWith("Net=")).Substring("Net=".Length);
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, "0.12", "-0.12")]
    [InlineData(MidpointRounding.AwayFromZero, "0.13", "-0.13")]
    public void A_computed_set_writes_the_value_CSharp_computes(MidpointRounding mode, string positive, string negative)
    {
        _server.Rows = new[] { (Guid.NewGuid(), "pos", 0.15m), (Guid.NewGuid(), "neg", -0.15m) };
        var store = new Store();
        store.SetSettings(Settings());

        store.Update(x => x.Code == "pos" || x.Code == "neg", NetFromGross(mode));

        WrittenNetOf("pos").Should().Be(positive, "0.15 / 1.2 = 0.125 rounds {0}", mode);
        WrittenNetOf("neg").Should().Be(negative);
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven, "0.12")]
    [InlineData(MidpointRounding.AwayFromZero, "0.13")]
    public async Task A_computed_set_writes_the_value_CSharp_computes_async(MidpointRounding mode, string expected)
    {
        _server.Rows = new[] { (Guid.NewGuid(), "pos", 0.15m) };
        var store = new AsyncStore();
        store.SetSettings(Settings());

        await store.UpdateAsync(x => x.Code == "pos", NetFromGross(mode));

        WrittenNetOf("pos").Should().Be(expected);
    }

    public void Dispose() => _server.Dispose();
}
