using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.REST;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.REST.Tests;

/// <summary>
/// Regression for CR-H031: the process-wide GetClient cache was a plain Dictionary mutated without
/// locking, so concurrent access could corrupt it or throw. It is now a ConcurrentDictionary.
/// </summary>
[Collection("RestClientCache")] // avoid interleaving with other tests that touch the static cache
public class RestClientCacheTests
{
    [Fact]
    public void GetClient_SameUri_ReturnsCachedInstance()
    {
        RestClient.ClearCache();
        var uri = $"https://cache-test-{Guid.NewGuid():N}.example.com";

        var a = RestClient.GetClient(uri);
        var b = RestClient.GetClient(uri);

        a.Should().BeSameAs(b);
        RestClient.RemoveClient(uri).Should().BeTrue();
        RestClient.RemoveClient(uri).Should().BeFalse();
    }

    [Fact]
    public async Task GetClient_ConcurrentAccess_DoesNotCorruptCache()
    {
        RestClient.ClearCache();
        var uris = Enumerable.Range(0, 40).Select(i => $"https://c{i}.example.com").ToArray();
        var seen = new ConcurrentBag<RestClient>();

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var round = 0; round < 200; round++)
            {
                foreach (var uri in uris)
                    seen.Add(RestClient.GetClient(uri));
            }
        }));

        var act = async () => await Task.WhenAll(tasks);

        await act.Should().NotThrowAsync();
        // Every URI resolves to a single shared instance regardless of thread interleaving.
        seen.Distinct().Should().HaveCount(uris.Length);

        RestClient.ClearCache();
    }
}
