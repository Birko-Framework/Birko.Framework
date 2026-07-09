using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.SOAP;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SOAP.Tests;

/// <summary>
/// Regressions for CR-M064 (the static client cache was a non-thread-safe Dictionary with
/// check-then-act GetClient) and CR-M065 (SoapServer.Dispose blocked on StopAsync via
/// GetAwaiter().GetResult()).
/// </summary>
public class SoapCacheAndServerTests
{
    [Fact]
    public void GetClient_ConcurrentSameUri_ReturnsSingleInstance_NoThrow()
    {
        SoapClient.ClearCache();
        const string uri = "https://soap.example.com/service.asmx";

        var results = new ConcurrentBag<SoapClient>();
        var act = () => Parallel.For(0, 64, _ => results.Add(SoapClient.GetClient(uri)));

        act.Should().NotThrow("concurrent GetClient must not race on a duplicate Add");
        results.Distinct().Should().HaveCount(1, "all callers share one cached client per URI");

        SoapClient.ClearCache();
    }

    [Fact]
    public void RemoveClient_And_ClearCache_AreConcurrencySafe()
    {
        SoapClient.ClearCache();
        for (var i = 0; i < 20; i++)
            SoapClient.GetClient($"https://soap.example.com/s{i}");

        var act = () => Parallel.For(0, 20, i => SoapClient.RemoveClient($"https://soap.example.com/s{i}"));
        act.Should().NotThrow();

        SoapClient.ClearCache();
    }

    [Fact]
    public void SoapServer_StopAndDispose_OnUnstarted_DoNotThrowOrBlock()
    {
        var server = new SoapServer();

        var act = () =>
        {
            server.Stop();     // idempotent, synchronous (CR-M065)
            server.Stop();
            server.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task SoapServer_StopAsync_OnUnstarted_Completes()
    {
        var server = new SoapServer();
        await server.StopAsync(); // must complete without hanging
        server.Dispose();
    }
}
