using FluentAssertions;
using Birko.Security.AspNetCore;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Tenant;

public class TenantContextAdapterTests
{
    [Fact]
    public void SetTenant_DelegatesToBirkoContext()
    {
        var birkoContext = new TestBirkoTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);
        var tenantId = Guid.NewGuid();

        adapter.SetTenant(tenantId, "Acme");

        adapter.HasTenant.Should().BeTrue();
        adapter.CurrentTenantId.Should().Be(tenantId);
        adapter.CurrentTenantName.Should().Be("Acme");
    }

    [Fact]
    public void ClearTenant_DelegatesToBirkoContext()
    {
        var birkoContext = new TestBirkoTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);
        adapter.SetTenant(Guid.NewGuid(), "Acme");

        adapter.ClearTenant();

        adapter.HasTenant.Should().BeFalse();
        adapter.CurrentTenantId.Should().BeNull();
        adapter.CurrentTenantName.Should().BeNull();
    }

    [Fact]
    public void InitialState_HasNoTenant()
    {
        var birkoContext = new TestBirkoTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);

        adapter.HasTenant.Should().BeFalse();
        adapter.CurrentTenantId.Should().BeNull();
        adapter.CurrentTenantName.Should().BeNull();
    }

    private class TestBirkoTenantContext : Birko.Data.Tenant.Models.ITenantContext
    {
        public Guid? CurrentTenantId { get; private set; }
        public string? CurrentTenantName { get; private set; }
        public bool HasTenant => CurrentTenantId.HasValue;

        public void SetTenant(Guid tenantId, string? tenantName = null)
        {
            CurrentTenantId = tenantId;
            CurrentTenantName = tenantName;
        }

        public void ClearTenant()
        {
            CurrentTenantId = null;
            CurrentTenantName = null;
        }

        public T WithTenant<T>(Guid tenantId, string? tenantName, Func<T> action)
        {
            SetTenant(tenantId, tenantName);
            try { return action(); }
            finally { ClearTenant(); }
        }

        public async Task<T?> WithTenantAsync<T>(Guid tenantId, string? tenantName, Func<Task<T>> action)
        {
            SetTenant(tenantId, tenantName);
            try { return await action(); }
            finally { ClearTenant(); }
        }

        public void WithTenant(Guid tenantId, string? tenantName, Action action)
        {
            SetTenant(tenantId, tenantName);
            try { action(); }
            finally { ClearTenant(); }
        }

        public async Task WithTenantAsync(Guid tenantId, string? tenantName, Func<Task> action)
        {
            SetTenant(tenantId, tenantName);
            try { await action(); }
            finally { ClearTenant(); }
        }
    }
}
