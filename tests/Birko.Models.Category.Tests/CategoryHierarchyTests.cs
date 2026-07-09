using System;
using Birko.Models.Contracts;
using FluentAssertions;
using Xunit;
using CategoryModel = Birko.Models.Category.Category;
using CategoryViewModel = Birko.Models.Category.ViewModels.Category;

namespace Birko.Models.Category.Tests;

/// <summary>
/// CR-H125: a Category edited through the ViewModel layer must not lose its hierarchy position.
/// The ViewModel carries only Path, so Model.LoadFrom must restore ParentGuid/Depth from it.
/// </summary>
public class CategoryHierarchyTests
{
    private static readonly Guid Root = Guid.NewGuid();
    private static readonly Guid Parent = Guid.NewGuid();
    private static readonly Guid Self = Guid.NewGuid();

    private static string PathOf(params Guid[] ids) => "/" + string.Join("/", ids);

    [Fact]
    public void LoadFrom_ViewModel_RestoresParentGuidAndDepthFromPath()
    {
        var vm = new CategoryViewModel
        {
            Title = "Smartphones",
            Slug = "smartphones",
            Description = "desc",
            Path = PathOf(Root, Parent, Self),
        };

        var model = new CategoryModel();
        model.LoadFrom(vm);

        model.Path.Should().Be(PathOf(Root, Parent, Self));
        model.Depth.Should().Be(2);
        model.ParentGuid.Should().Be(Parent);
    }

    [Fact]
    public void LoadFrom_ViewModel_RootCategory_HasNoParentAndZeroDepth()
    {
        var vm = new CategoryViewModel
        {
            Title = "Electronics",
            Slug = "electronics",
            Description = "root",
            Path = PathOf(Self),
        };

        var model = new CategoryModel();
        model.LoadFrom(vm);

        model.Depth.Should().Be(0);
        model.ParentGuid.Should().BeNull();
    }

    [Fact]
    public void RoundTrip_Model_ViewModel_Model_PreservesHierarchyPosition()
    {
        var original = new CategoryModel
        {
            Title = "Smartphones",
            Slug = "smartphones",
            Description = "desc",
            Path = PathOf(Root, Parent, Self),
            ParentGuid = Parent,
            Depth = 2,
        };

        var vm = new CategoryViewModel();
        vm.LoadFrom(original);

        var restored = new CategoryModel();
        restored.LoadFrom(vm);

        restored.ParentGuid.Should().Be(original.ParentGuid);
        restored.Depth.Should().Be(original.Depth);
        restored.Path.Should().Be(original.Path);
    }
}

/// <summary>
/// CR-H125: the reusable derive helper on HierarchyHelper (IHierarchical-only, no persistence base).
/// </summary>
public class DeriveParentAndDepthFromPathTests
{
    private sealed class Node : IHierarchical
    {
        public Guid? ParentGuid { get; set; }
        public string Path { get; set; } = string.Empty;
        public int Depth { get; set; }
    }

    [Fact]
    public void Nested_DerivesParentAndDepth()
    {
        var parent = Guid.NewGuid();
        var self = Guid.NewGuid();
        var node = new Node { Path = $"/{Guid.NewGuid()}/{parent}/{self}" };

        HierarchyHelper.DeriveParentAndDepthFromPath(node);

        node.Depth.Should().Be(2);
        node.ParentGuid.Should().Be(parent);
    }

    [Fact]
    public void Root_HasNullParentAndZeroDepth()
    {
        var node = new Node { Path = $"/{Guid.NewGuid()}" };

        HierarchyHelper.DeriveParentAndDepthFromPath(node);

        node.Depth.Should().Be(0);
        node.ParentGuid.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void EmptyOrSeparatorOnlyPath_ResetsToRootDefaults(string path)
    {
        var node = new Node { Path = path, Depth = 5, ParentGuid = Guid.NewGuid() };

        HierarchyHelper.DeriveParentAndDepthFromPath(node);

        node.Depth.Should().Be(0);
        node.ParentGuid.Should().BeNull();
    }

    [Fact]
    public void NullNode_DoesNotThrow()
    {
        var act = () => HierarchyHelper.DeriveParentAndDepthFromPath(null!);
        act.Should().NotThrow();
    }
}
