using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.REST;
using FluentAssertions;
using Xunit;
using System.IO;

namespace Birko.Communication.REST.Tests;

/// <summary>
/// Regression for CR-H031: the process-wide GetClient cache was a plain Dictionary mutated without
/// locking, so concurrent access could corrupt it or throw. It is now a ConcurrentDictionary.
/// </summary>
[Collection("RestClientCache")] // avoid interleaving with other tests that touch the static cache
public class RestClientCacheTests
{
    /// <summary>
    /// Every test class that touches the static client cache must name the same xUnit collection.
    /// </summary>
    /// <remarks>
    /// TASK-459: the attribute above was on this class alone, which serialises nothing — xUnit runs
    /// classes within a collection sequentially and different collections in PARALLEL, so
    /// <c>RestClientTests.ClearCache_EvictsAllEntries</c> was free to wipe the cache in the middle of
    /// <c>GetClient_ConcurrentAccess_DoesNotCorruptCache</c>. That test then saw exactly 80 distinct
    /// instances where it expected 40 — the same 40 URIs resolved on either side of the eviction.
    /// It failed 2 of 4 CI runs and never once locally, because the interleaving needs the classes
    /// to genuinely overlap, so a behavioural test cannot be relied on to catch a regression here.
    /// <para>
    /// A source scan can. It is deterministic, and it catches the NEXT class to touch the cache
    /// rather than waiting for CI to get unlucky.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_class_touching_the_static_cache_shares_the_collection()
    {
        var dir = TestSourceDirectory();
        dir.Should().NotBeNull("the source scan must actually find this test project");

        // Comments are stripped FIRST, and that is load-bearing in both directions. The files here
        // explain this defect at length, so their prose contains both the forbidden calls and the
        // required attribute; scanning raw text made this guard pass while the attribute was
        // actually absent — measured, the mutation reded 0 tests. A guard that must quote what it
        // checks for has to read code, not commentary.
        static string CodeOnly(string src) => string.Join('\n', src
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        var offenders = Directory.EnumerateFiles(dir!, "*.cs", SearchOption.AllDirectories)
            .Select(f => new { File = Path.GetFileName(f), Code = CodeOnly(File.ReadAllText(f)) })
            .Where(x => x.Code.Contains("RestClient." + "ClearCache(", StringComparison.Ordinal)
                     || x.Code.Contains("RestClient." + "GetClient(", StringComparison.Ordinal))
            .Where(x => !x.Code.Contains("[Collection(\"RestClientCache\")]", StringComparison.Ordinal))
            .Select(x => x.File)
            .ToList();

        offenders.Should().BeEmpty(
            "a class that touches the static RestClient cache must carry " +
            "[Collection(\"RestClientCache\")], or xUnit runs it in parallel with the cache tests");
    }

    private static string? TestSourceDirectory()
    {
        // Same tolerant walk-up the other source scans use: probe every ancestor, trying the level
        // itself and a "Framework" child. A fixed depth breaks whenever the layout moves.
        for (var probe = new DirectoryInfo(AppContext.BaseDirectory); probe != null; probe = probe.Parent)
        {
            foreach (var root in new[] { probe.FullName, Path.Combine(probe.FullName, "Framework") })
            {
                var candidate = Path.Combine(root, "tests", "Birko.Communication.REST.Tests");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

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
