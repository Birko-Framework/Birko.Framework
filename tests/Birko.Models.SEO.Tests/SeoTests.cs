using System;
using System.Linq;
using Birko.Models.SEO.Filters;
using FluentAssertions;
using Xunit;
using SeoModel = Birko.Models.SEO.SEO;
using SeoVm = Birko.Models.SEO.ViewModels.SEO;

namespace Birko.Models.SEO.Tests;

/// <summary>
/// CR-M224: Birko.Models.SEO had no test project. Covers the model/VM LoadFrom round-trips (incl. the
/// log/identity fields via base.LoadFrom), the PropertyChanged → SEOObjectProperty aggregation, the
/// SEO&lt;T&gt; guid filter, and SEOByPath exact vs prefix vs empty-path behavior.
/// </summary>
public class SeoTests
{
    [Fact]
    public void Model_LoadFrom_ViewModel_CopiesFieldsAndLog()
    {
        var guid = Guid.NewGuid();
        var created = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var vm = new SeoVm { Guid = guid, CreatedAt = created, Title = "T", Path = "/p", Description = "D" };

        var model = new SeoModel();
        model.LoadFrom(vm);

        model.Title.Should().Be("T");
        model.Path.Should().Be("/p");
        model.Description.Should().Be("D");
        model.Guid.Should().Be(guid);
        model.CreatedAt.Should().Be(created);
    }

    [Fact]
    public void ViewModel_LoadFrom_Model_RoundTrips()
    {
        var model = new SeoModel { Guid = Guid.NewGuid(), Title = "T", Path = "/p", Description = "D" };
        var vm = new SeoVm();
        vm.LoadFrom(model);

        vm.Title.Should().Be("T");
        vm.Path.Should().Be("/p");
        vm.Description.Should().Be("D");
        vm.Guid.Should().Be(model.Guid);
    }

    [Fact]
    public void ViewModel_PropertyChange_RaisesSeoObjectAggregate()
    {
        var vm = new SeoVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == SeoVm.SEOObjectProperty) raised = true; };

        vm.Title = "changed";

        raised.Should().BeTrue("a Title change must fan out to the SEO aggregate property");
    }

    [Fact]
    public void SEOByPath_Exact_MatchesOnlyExactPath()
    {
        var filter = new SEOByPath<SeoModel>("/blog", exact: true).Filter()!.Compile();
        filter(new SeoModel { Path = "/blog" }).Should().BeTrue();
        filter(new SeoModel { Path = "/blog/post" }).Should().BeFalse();
    }

    [Fact]
    public void SEOByPath_Prefix_MatchesDescendants()
    {
        var filter = new SEOByPath<SeoModel>("/blog").Filter()!.Compile();
        filter(new SeoModel { Path = "/blog" }).Should().BeTrue();
        filter(new SeoModel { Path = "/blog/post" }).Should().BeTrue();
        filter(new SeoModel { Path = "/other" }).Should().BeFalse();
    }

    [Fact]
    public void SEOByPath_EmptyPath_ReturnsNullFilter()
    {
        new SEOByPath<SeoModel>(string.Empty).Filter().Should().BeNull();
    }

    [Fact]
    public void SEOGuidFilter_MatchesByGuid()
    {
        var guid = Guid.NewGuid();
        var filter = new SEO<SeoModel>(guid).Filter()!.Compile();
        filter(new SeoModel { Guid = guid }).Should().BeTrue();
        filter(new SeoModel { Guid = Guid.NewGuid() }).Should().BeFalse();
    }

    [Fact]
    public void SEOByPath_Prefix_NullEntityPath_DoesNotThrow_AndReturnsFalse()
    {
        // CR-L319: an entity with a null Path (default/partially-loaded) must not NRE under LINQ-to-objects.
        var filter = new SEOByPath<SeoModel>("/blog").Filter()!.Compile();

        System.Action act = () => filter(new SeoModel { Path = null! });

        act.Should().NotThrow();
        filter(new SeoModel { Path = null! }).Should().BeFalse();
    }

    [Fact]
    public void ViewModel_PathSetter_SameValue_DoesNotRaisePropertyChanged()
    {
        // CR-L320.
        var vm = new SeoVm { Path = "/a" };
        var raised = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Path = "/a";

        raised.Should().NotContain(SeoVm.PathProperty);
        raised.Should().NotContain(SeoVm.SEOObjectProperty);
    }

    [Fact]
    public void ViewModel_PathSetter_NewValue_RaisesPropertyChanged()
    {
        var vm = new SeoVm { Path = "/a" };
        var raised = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Path = "/b";

        raised.Should().Contain(SeoVm.PathProperty);
    }

    [Fact]
    public void LoadFrom_Null_DoesNotThrow()
    {
        // CR-L321: the null guard runs before base.LoadFrom in all overloads.
        var model = new SeoModel();
        var vm = new SeoVm();

        ((System.Action)(() => model.LoadFrom((SeoVm)null!))).Should().NotThrow();
        ((System.Action)(() => vm.LoadFrom((SeoModel)null!))).Should().NotThrow();
        ((System.Action)(() => vm.LoadFrom((SeoVm)null!))).Should().NotThrow();
    }
}
