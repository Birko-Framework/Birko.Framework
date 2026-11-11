using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public class T908OfflinePoint : AbstractModel
{
    public string? Kind { get; set; }
}

/// <summary>
/// TASK-908: <see cref="Settings.RequestTimeout"/> reaches the client every store and repository builds, the connection
/// string's own <c>timeout</c> keeps working when it is not set, and a value the client cannot honour is refused.
/// </summary>
public sealed class Task908RequestTimeoutTests
{
    private const string Url = "http://localhost:8086";

    [Fact]
    public void Unset_it_leaves_the_client_default_of_10_seconds()
    {
        var settings = new Settings(Url, "b", "t", "o");

        settings.RequestTimeout.Should().BeNull();
        settings.GetClientOptions().Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void A_set_timeout_reaches_the_client_options_with_the_url_and_token()
    {
        var settings = new Settings(Url, "b", "secret", "o") { RequestTimeout = TimeSpan.FromSeconds(90) };

        var options = settings.GetClientOptions();

        options.Timeout.Should().Be(TimeSpan.FromSeconds(90));
        options.Token.Should().Be("secret");
        options.Url.Should().StartWith(Url);
    }

    [Fact]
    public void Unset_the_connection_strings_timeout_still_applies()
    {
        new Settings(Url + "?timeout=5000", "b", "t", "o").GetClientOptions().Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("?timeout=5000")]
    [InlineData("?TIMEOUT=5000")]
    [InlineData("?org=o&Timeout=10000")]
    public void Set_alongside_a_connection_string_timeout_it_is_refused_rather_than_picking_one(string query)
    {
        var settings = new Settings(Url + query, "b", "t", "o") { RequestTimeout = TimeSpan.FromSeconds(30) };

        var act = () => settings.GetClientOptions();

        act.Should().Throw<InvalidOperationException>().WithMessage("*RequestTimeout*connection string*timeout*");
        using var store = new AsyncInfluxDBStore<T908OfflinePoint>();
        store.Invoking(s => s.SetSettings(settings)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_blank_connection_string_timeout_is_not_a_second_source()
    {
        // The client ignores a blank value, so it is not a second source.
        new Settings(Url + "?timeout=", "b", "t", "o") { RequestTimeout = TimeSpan.FromSeconds(30) }
            .GetClientOptions().Timeout.Should().Be(TimeSpan.FromSeconds(30));
    }

    public static IEnumerable<object[]> Refused => new[]
    {
        new object[] { TimeSpan.Zero },
        new object[] { TimeSpan.FromMilliseconds(-5) },
        new object[] { TimeSpan.FromMilliseconds(uint.MaxValue) },
        new object[] { TimeSpan.MaxValue },
        new object[] { TimeSpan.MinValue },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_value_the_client_cannot_honour_is_refused_when_set(TimeSpan value)
    {
        var settings = new Settings(Url, "b", "t", "o");

        var act = () => settings.RequestTimeout = value;

        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*RequestTimeout*");
        settings.RequestTimeout.Should().BeNull();
    }

    public static IEnumerable<object[]> Accepted => new[]
    {
        new object[] { TimeSpan.FromMilliseconds(1) },
        new object[] { TimeSpan.FromMilliseconds(uint.MaxValue - 1.0) },
        new object[] { Timeout.InfiniteTimeSpan },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void The_bounds_and_infinite_are_accepted(TimeSpan value)
    {
        var settings = new Settings(Url, "b", "t", "o") { RequestTimeout = value };

        settings.GetClientOptions().Timeout.Should().Be(value);
    }

    [Fact]
    public void Null_can_be_set_back()
    {
        var settings = new Settings(Url, "b", "t", "o") { RequestTimeout = TimeSpan.FromSeconds(3) };
        settings.RequestTimeout = null;

        settings.GetClientOptions().Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void LoadFrom_copies_it()
    {
        var target = new Settings();
        target.LoadFrom(new Settings(Url, "b", "t", "o") { RequestTimeout = TimeSpan.FromMinutes(2) });

        target.RequestTimeout.Should().Be(TimeSpan.FromMinutes(2));
    }

    private sealed class DerivedTokenSettings : Settings
    {
        public DerivedTokenSettings() : base(Url, "b") { }
        public override string GetConnectionString()
        {
            Token = "user:password";   // as Symbio.Affiliate's settings do (InfluxDB 1.x compatibility)
            return base.GetConnectionString();
        }
    }

    [Fact]
    public void A_token_derived_in_GetConnectionString_still_reaches_the_client()
    {
        new DerivedTokenSettings().GetClientOptions().Token.Should().Be("user:password");
    }

    /// <summary>A server that accepts connections and never answers.</summary>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _held = new();
        public SilentServer()
        {
            _listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var client = await _listener.AcceptTcpClientAsync();
                        lock (_held) _held.Add(client);
                    }
                }
                catch (Exception) { }
            });
        }
        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        public void Dispose()
        {
            _listener.Stop();
            lock (_held) foreach (var c in _held) c.Dispose();
        }
    }

    [Fact]
    public async Task On_the_wire_the_async_store_gives_up_at_the_set_timeout_not_at_10_seconds()
    {
        using var server = new SilentServer();
        using var store = new AsyncInfluxDBStore<T908OfflinePoint>();
        store.SetSettings(new Settings(server.Url, "b", "t", "o") { RequestTimeout = TimeSpan.FromMilliseconds(500) });

        var watch = Stopwatch.StartNew();
        var act = async () => await store.ReadAsync(CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void On_the_wire_the_sync_store_gives_up_at_the_set_timeout_not_at_10_seconds()
    {
        using var server = new SilentServer();
        using var store = new InfluxDBStore<T908OfflinePoint>();
        store.SetSettings(new Settings(server.Url, "b", "t", "o") { RequestTimeout = TimeSpan.FromMilliseconds(500) });

        var watch = Stopwatch.StartNew();
        var act = () => store.Read().ToList();

        act.Should().Throw<Exception>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }
}
