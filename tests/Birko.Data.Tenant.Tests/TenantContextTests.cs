using System;
using System.Threading.Tasks;
using Birko.Data.Tenant.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tenant.Tests;

/// <summary>
/// CR-H108: coverage for TenantContext save/restore semantics (part of the previously-untested
/// Birko.Data.Tenant surface).
/// </summary>
public class TenantContextTests
{
    [Fact]
    public void WithTenant_SetsDuringAction_AndRestoresAfter()
    {
        var ctx = new TenantContext();
        var outer = Guid.NewGuid();
        ctx.SetTenant(outer, "outer");

        var inner = Guid.NewGuid();
        ctx.WithTenant(inner, "inner", () =>
        {
            ctx.CurrentTenantGuid.Should().Be(inner);
            ctx.CurrentTenantName.Should().Be("inner");
        });

        ctx.CurrentTenantGuid.Should().Be(outer, "the previous tenant is restored");
        ctx.CurrentTenantName.Should().Be("outer");
    }

    [Fact]
    public void WithTenant_RestoresOnException()
    {
        var ctx = new TenantContext();
        var outer = Guid.NewGuid();
        ctx.SetTenant(outer);

        Action act = () => ctx.WithTenant(Guid.NewGuid(), null, () => throw new InvalidOperationException());

        act.Should().Throw<InvalidOperationException>();
        ctx.CurrentTenantGuid.Should().Be(outer, "restore runs in finally even on exception");
    }

    [Fact]
    public void WithTenant_FromNoTenant_RestoresToNoTenant()
    {
        var ctx = new TenantContext();
        ctx.HasTenant.Should().BeFalse();

        ctx.WithTenant(Guid.NewGuid(), null, () => ctx.HasTenant.Should().BeTrue());

        ctx.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task WithTenantAsync_RestoresAfterAwait()
    {
        var ctx = new TenantContext();
        var outer = Guid.NewGuid();
        ctx.SetTenant(outer);

        var inner = Guid.NewGuid();
        await ctx.WithTenantAsync(inner, null, async () =>
        {
            await Task.Delay(5);
            ctx.CurrentTenantGuid.Should().Be(inner);
        });

        ctx.CurrentTenantGuid.Should().Be(outer);
    }

    [Fact]
    public void ClearTenant_RemovesTenant()
    {
        var ctx = new TenantContext();
        ctx.SetTenant(Guid.NewGuid());
        ctx.ClearTenant();
        ctx.HasTenant.Should().BeFalse();
        ctx.CurrentTenantGuid.Should().BeNull();
    }
}
