using System;
using System.Linq;
using Birko.Models.Contracts;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Contracts.Tests;

/// <summary>
/// TASK-444 — <see cref="IBatchable"/> shipped with <c>BatchNumber</c> as non-nullable <c>string</c> and
/// had <b>zero implementors</b> for its whole life. Batch is optional in every real inventory — most
/// stock carries none — so as declared the contract was unimplementable by exactly the entities it was
/// written for, which is the most plausible reason nobody implemented it: FisData rolled a near-duplicate
/// <c>IBatchTracked</c> instead, and Symbio's batch-bearing entities all declare <c>string?</c>.
/// </summary>
public class BatchableTests
{
    private sealed class Probe : IBatchable
    {
        public string? BatchNumber { get; set; }
        public DateTime? ExpiryDate { get; set; }
    }

    [Fact]
    public void BatchNumber_is_nullable()
    {
        // The change itself. Declared as `string`, a `string?` implementation is a nullability warning
        // and the obvious implementors could not satisfy it cleanly.
        var property = typeof(IBatchable).GetProperty(nameof(IBatchable.BatchNumber))!;

        new System.Reflection.NullabilityInfoContext()
            .Create(property).WriteState
            .Should().Be(System.Reflection.NullabilityState.Nullable);
    }

    [Fact]
    public void Both_members_accept_null()
    {
        // Untracked stock: no batch, no expiry. This is the common row, not the edge case.
        IBatchable probe = new Probe { BatchNumber = null, ExpiryDate = null };

        probe.BatchNumber.Should().BeNull();
        probe.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void A_batch_without_an_expiry_is_representable()
    {
        // A serial or lot number that never expires — the reason ExpiryDate is separately nullable
        // rather than implied by the presence of a batch.
        IBatchable probe = new Probe { BatchNumber = "L-1", ExpiryDate = null };

        probe.BatchNumber.Should().Be("L-1");
        probe.ExpiryDate.Should().BeNull();
    }

    [Fact]
    public void The_contract_is_exactly_these_two_members()
    {
        // Guards against the contract quietly growing. Anything else about a batch (supplier, received
        // date, cost) belongs to a batch entity, not to "this row can carry a batch".
        typeof(IBatchable).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo(new[] { nameof(IBatchable.BatchNumber), nameof(IBatchable.ExpiryDate) });
    }
}
