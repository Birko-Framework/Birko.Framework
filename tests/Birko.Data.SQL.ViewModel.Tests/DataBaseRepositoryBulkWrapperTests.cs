using System;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.Tenant.Models;
using Birko.Data.Tenant.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.ViewModel.Tests;

public class TenantRow : AbstractModel, ITenant
{
    public Guid TenantGuid { get; set; }
    public string? TenantName { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TenantRowViewModel : ILoadable<TenantRow>
{
    public string Name { get; set; } = string.Empty;
    public void LoadFrom(TenantRow data) => Name = data.Name;
}

public class TenantRowRepo : Birko.Data.SQL.Repositories.DataBaseRepository<SqLiteConnector, TenantRowViewModel, TenantRow>
{
    public TenantRowRepo(Birko.Data.Stores.IStore<TenantRow>? store) : base(store) { }
    protected override void MapToModel(TenantRowViewModel source, TenantRow target) => target.Name = source.Name;
}

/// <summary>
/// SH-M208 (TASK-162). The constructor checked the INNERMOST store while every inherited bulk method checks the OUTERMOST,
/// so a non-bulk wrapper around a SQL bulk store constructed fine and every bulk call then threw ArgumentException.
/// </summary>
public class DataBaseRepositoryBulkWrapperTests
{
    [Fact]
    public void A_non_bulk_wrapper_is_refused_at_construction()
    {
        var nonBulk = new TenantStoreWrapper<DataBaseBulkStore<SqLiteConnector, TenantRow>, TenantRow>(
            new DataBaseBulkStore<SqLiteConnector, TenantRow>(), new TenantContext());
        var act = () => new TenantRowRepo(nonBulk);
        act.Should().Throw<ArgumentException>().WithMessage("*bulk*");
    }

    [Fact]
    public void The_bulk_wrapper_is_accepted()
    {
        var bulk = new TenantBulkStoreWrapper<DataBaseBulkStore<SqLiteConnector, TenantRow>, TenantRow>(
            new DataBaseBulkStore<SqLiteConnector, TenantRow>(), new TenantContext());
        new TenantRowRepo(bulk).Should().NotBeNull();
    }
}
