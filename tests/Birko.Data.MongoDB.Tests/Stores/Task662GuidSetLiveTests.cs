using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.MongoDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.MongoDB.Tests.Stores;

/// <summary>
/// TASK-662, live MongoDB: a <c>$set</c> of a <see cref="Guid"/>, a nullable Guid and a null through
/// <see cref="PropertyUpdate{T}"/>. On HEAD the Guid threw before anything was sent. The value must read back equal, so
/// the representation is the one the document was written with. Gated on <c>BIRKO_MONGO_HOST</c>; set
/// <c>BIRKO_REQUIRE_LIVE</c> to make its absence a failure.
/// </summary>
public class Task662GuidSetLiveTests(ITestOutputHelper output)
{
    public class ClaimDoc : AbstractModel
    {
        public int Status { get; set; }
        public Guid Owner { get; set; }
        public Guid? Claim { get; set; }
        public int? Budget { get; set; }
    }

    private string? Host()
    {
        var host = Environment.GetEnvironmentVariable("BIRKO_MONGO_HOST");
        if (!string.IsNullOrWhiteSpace(host)) return host;
        const string message = "SKIPPED: no live MongoDB. Set BIRKO_MONGO_HOST; set BIRKO_REQUIRE_LIVE to make its absence a failure.";
        output.WriteLine(message);
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BIRKO_REQUIRE_LIVE")))
            throw new InvalidOperationException(message);
        return null;
    }

    [Fact]
    public async Task A_Guid_a_nullable_Guid_and_a_null_are_set_and_read_back()
    {
        var host = Host();
        if (host == null) return;
        var store = new AsyncMongoDBStore<ClaimDoc>();
        store.SetSettings(new Settings(host, "birko_task662_" + Guid.NewGuid().ToString("N")));
        var doc = new ClaimDoc { Status = 1, Owner = Guid.NewGuid(), Budget = 3 };
        await store.CreateAsync(doc);

        var owner = Guid.NewGuid();
        var claim = Guid.NewGuid();
        await store.UpdateAsync(x => x.Guid == doc.Guid && x.Status == 1,
            new PropertyUpdate<ClaimDoc>().Set(x => x.Owner, owner).Set(x => x.Claim, claim).Set(x => x.Budget, null).Set(x => x.Status, 2));

        var back = (await store.ReadAsync(x => x.Guid == doc.Guid)).Single();
        back.Owner.Should().Be(owner);
        back.Claim.Should().Be(claim);
        back.Budget.Should().BeNull();
        back.Status.Should().Be(2);
        (await store.ReadAsync(x => x.Claim == claim)).Should().ContainSingle("a filter on the written Guid finds it");
    }
}
