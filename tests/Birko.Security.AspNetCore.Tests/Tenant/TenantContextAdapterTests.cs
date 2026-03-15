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
        var tenantGuid = Guid.NewGuid();

        adapter.SetTenant(tenantGuid, "Acme");

        adapter.HasTenant.Should().BeTrue();
        adapter.CurrentTenantGuid.Should().Be(tenantGuid);
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
        adapter.CurrentTenantGuid.Should().BeNull();
        adapter.CurrentTenantName.Should().BeNull();
    }

    [Fact]
    public void InitialState_HasNoTenant()
    {
        var birkoContext = new TestBirkoTenantContext();
        var adapter = new TenantContextAdapter(birkoContext);

        adapter.HasTenant.Should().BeFalse();
        adapter.CurrentTenantGuid.Should().BeNull();
        adapter.CurrentTenantName.Should().BeNull();
    }

    private class TestBirkoTenantContext : Birko.Data.Tenant.Models.ITenantContext
    {
        public Guid? CurrentTenantGuid { get; private set; }
        public string? CurrentTenantName { get; private set; }
        public bool HasTenant => CurrentTenantGuid.HasValue;

        public void SetTenant(Guid tenantGuid, string? tenantName = null)
        {
            CurrentTenantGuid = tenantGuid;
            CurrentTenantName = tenantName;
        }

        public void ClearTenant()
        {
            CurrentTenantGuid = null;
            CurrentTenantName = null;
        }

        public T WithTenant<T>(Guid tenantGuid, string? tenantName, Func<T> action)
        {
            SetTenant(tenantGuid, tenantName);
            try { return action(); }
            finally { ClearTenant(); }
        }

        public async Task<T?> WithTenantAsync<T>(Guid tenantGuid, string? tenantName, Func<Task<T>> action)
        {
            SetTenant(tenantGuid, tenantName);
            try { return await action(); }
            finally { ClearTenant(); }
        }

        public void WithTenant(Guid tenantGuid, string? tenantName, Action action)
        {
            SetTenant(tenantGuid, tenantName);
            try { action(); }
            finally { ClearTenant(); }
        }

        public async Task WithTenantAsync(Guid tenantGuid, string? tenantName, Func<Task> action)
        {
            SetTenant(tenantGuid, tenantName);
            try { await action(); }
            finally { ClearTenant(); }
        }
    }
}
