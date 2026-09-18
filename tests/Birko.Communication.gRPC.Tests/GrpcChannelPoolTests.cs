using System;
using Birko.Communication.gRPC;
using FluentAssertions;
using Xunit;
using System.IO;
using System.Linq;

namespace Birko.Communication.gRPC.Tests;

[Collection("ChannelPool")]
public class GrpcChannelPoolTests : IDisposable
{
    public GrpcChannelPoolTests() => GrpcChannelPool.Clear();

    public void Dispose() => GrpcChannelPool.Clear();

    /// <summary>
    /// Every test class that touches the static channel pool must name the same xUnit collection.
    /// </summary>
    /// <remarks>
    /// TASK-459: the attribute above was on this class alone, which serialises nothing — xUnit runs
    /// classes within a collection sequentially and different collections in PARALLEL. This class's
    /// ctor and Dispose both call <c>GrpcChannelPool.Clear()</c>, which DISPOSES every pooled
    /// channel, so it was free to land inside
    /// <c>GrpcClientFactoryTests.CreateClient_From_Settings_Uses_Pooled_Channel</c> — which failed
    /// with <c>ObjectDisposedException</c> on a channel it legitimately held. 1 of 5 CI runs, never
    /// locally, because the classes have to genuinely overlap: a behavioural test cannot guard it.
    /// <para>
    /// Second instance of this exact shape in one sweep (see the REST client cache), so the guard is
    /// a source scan, which is deterministic and catches the NEXT class rather than waiting for CI
    /// to get unlucky. Comments are stripped first — the REST version of this scan initially passed
    /// while the attribute was absent, because these files explain the defect and their prose
    /// contains the very strings being matched.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_class_touching_the_static_pool_shares_the_collection()
    {
        var dir = TestSourceDirectory();
        dir.Should().NotBeNull("the source scan must actually find this test project");

        static string CodeOnly(string src) => string.Join('\n', src
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var offenders = Directory.EnumerateFiles(dir!, "*.cs", SearchOption.AllDirectories)
            .Select(f => new { File = Path.GetFileName(f), Code = CodeOnly(File.ReadAllText(f)) })
            .Where(x => x.Code.Contains("GrpcChannelPool" + ".", StringComparison.Ordinal)
                     || x.Code.Contains("GrpcClientFactory" + ".", StringComparison.Ordinal))
            .Where(x => !x.Code.Contains("[Collection(\"ChannelPool\")]", StringComparison.Ordinal))
            .Select(x => x.File)
            .ToList();

        offenders.Should().BeEmpty(
            "a class that touches the static GrpcChannelPool must carry [Collection(\"ChannelPool\")], " +
            "or xUnit runs it in parallel with the class that disposes every pooled channel");
    }

    private static string? TestSourceDirectory()
    {
        // Tolerant walk-up: probe every ancestor, trying the level itself and a "Framework" child.
        // A fixed depth breaks whenever the layout moves.
        for (var probe = new DirectoryInfo(AppContext.BaseDirectory); probe != null; probe = probe.Parent)
        {
            foreach (var root in new[] { probe.FullName, Path.Combine(probe.FullName, "Framework") })
            {
                var candidate = Path.Combine(root, "tests", "Birko.Communication.gRPC.Tests");
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    [Fact]
    public void GetChannel_Caches_Per_Endpoint()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5001" };

        var first = GrpcChannelPool.GetChannel(settings);
        var second = GrpcChannelPool.GetChannel(settings);

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void GetChannel_Different_Endpoints_Are_Distinct()
    {
        var a = GrpcChannelPool.GetChannel(new GrpcSettings { Endpoint = "https://localhost:5001" });
        var b = GrpcChannelPool.GetChannel(new GrpcSettings { Endpoint = "https://localhost:5002" });

        a.Should().NotBeSameAs(b);
    }

    [Fact]
    public void GetChannel_Throws_When_Endpoint_Missing()
    {
        var act = () => GrpcChannelPool.GetChannel(new GrpcSettings());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetChannel_Throws_When_Settings_Null()
    {
        var act = () => GrpcChannelPool.GetChannel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Remove_Evicts_Channel()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5003" };
        GrpcChannelPool.GetChannel(settings);

        GrpcChannelPool.Remove("https://localhost:5003").Should().BeTrue();
        GrpcChannelPool.Remove("https://localhost:5003").Should().BeFalse();
    }

    [Fact]
    public void Remove_After_Eviction_Creates_New_Instance()
    {
        var settings = new GrpcSettings { Endpoint = "https://localhost:5004" };

        var first = GrpcChannelPool.GetChannel(settings);
        GrpcChannelPool.Remove("https://localhost:5004");
        var second = GrpcChannelPool.GetChannel(settings);

        first.Should().NotBeSameAs(second);
    }
}
