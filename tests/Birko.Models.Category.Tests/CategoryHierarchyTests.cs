using System;
using System.Collections.Generic;
using Birko.Models.Contracts;
using FluentAssertions;
using Xunit;
using CategoryModel = Birko.Models.Category.Category;
using CategoryViewModel = Birko.Models.Category.ViewModels.Category;

namespace Birko.Models.Category.Tests;

/// <summary>
/// CR-L303/L304: ViewModel setter change-notification guards and null-safe LoadFrom ordering.
/// </summary>
public class CategoryViewModelTests
{
    [Fact]
    public void PathSetter_SameValue_DoesNotRaisePropertyChanged()
    {
        // CR-L304: setting Path to its current value must not fire PropertyChanged (or the cascaded
        // "Category" object notification).
        var vm = new CategoryViewModel { Path = "/a/b" };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Path = "/a/b";

        raised.Should().NotContain(CategoryViewModel.PathProperty);
        raised.Should().NotContain(CategoryViewModel.CategoryObjectProperty);
    }

    [Fact]
    public void PathSetter_NewValue_RaisesPropertyChanged()
    {
        var vm = new CategoryViewModel { Path = "/a" };
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Path = "/a/b";

        raised.Should().Contain(CategoryViewModel.PathProperty);
        raised.Should().Contain(CategoryViewModel.CategoryObjectProperty);
    }

    [Fact]
    public void LoadFrom_Null_DoesNotThrow()
    {
        // CR-L303: the null guard now runs before base.LoadFrom, so all overloads short-circuit safely.
        var model = new CategoryModel();
        var vm = new CategoryViewModel();

        ((Action)(() => model.LoadFrom((CategoryViewModel)null!))).Should().NotThrow();
        ((Action)(() => vm.LoadFrom((CategoryModel)null!))).Should().NotThrow();
        ((Action)(() => vm.LoadFrom((CategoryViewModel)null!))).Should().NotThrow();
    }
}

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

    // CR-M214: GetSlugSource feeds the slug generator.
    [Fact]
    public void GetSlugSource_ReturnsTitle()
    {
        new CategoryModel { Title = "Smart Phones" }.GetSlugSource().Should().Be("Smart Phones");
    }
}
