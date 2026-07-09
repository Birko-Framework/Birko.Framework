using System;
using FluentAssertions;
using Xunit;
using StorageLocationModel = Birko.Models.Inventory.StorageLocation;
using StorageLocationViewModel = Birko.Models.Inventory.ViewModels.StorageLocation;

namespace Birko.Models.Inventory.Tests;

/// <summary>
/// CR-H129: StorageLocation.Depth (the coupled half of the materialized-path scheme) must
/// round-trip through the ViewModel, not zero out while Path is loaded.
/// </summary>
public class StorageLocationTests
{
    [Fact]
    public void LoadFrom_RoundTripsDepthAlongsidePath()
    {
        var parent = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var vm = new StorageLocationViewModel
        {
            Title = "Bin A1",
            SortOrder = 3,
            ParentGuid = parent,
            Path = $"/{Guid.NewGuid()}/{parent}/{Guid.NewGuid()}",
            Depth = 2,
            TenantGuid = tenant,
        };

        var model = new StorageLocationModel();
        model.LoadFrom(vm);

        model.Title.Should().Be("Bin A1");
        model.SortOrder.Should().Be(3);
        model.ParentGuid.Should().Be(parent);
        model.Path.Should().Be(vm.Path);
        model.Depth.Should().Be(2, "Depth must round-trip so it stays consistent with Path");
        model.TenantGuid.Should().Be(tenant);
    }

    [Fact]
    public void LoadFrom_RootLocation_DepthZero()
    {
        var vm = new StorageLocationViewModel
        {
            Title = "Zone",
            Path = $"/{Guid.NewGuid()}",
            Depth = 0,
        };

        var model = new StorageLocationModel();
        model.LoadFrom(vm);

        model.Depth.Should().Be(0);
        model.Path.Should().Be(vm.Path);
    }
}
