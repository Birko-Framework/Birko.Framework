using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Filters;
using Birko.Data.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

public class FilterTests
{
    private sealed class TestModel : AbstractModel { }

    [Fact]
    public void ModelByGuid_matches_only_the_given_guid()
    {
        var target = Guid.NewGuid();
        var predicate = new ModelByGuid<TestModel>(target).Filter().Compile();

        predicate(new TestModel { Guid = target }).Should().BeTrue();
        predicate(new TestModel { Guid = Guid.NewGuid() }).Should().BeFalse();
    }

    [Fact]
    public void ModelsByGuid_matches_any_guid_in_the_set()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var filter = new ModelsByGuid<TestModel>(new[] { a, b }).Filter();
        filter.Should().NotBeNull();
        var predicate = filter!.Compile();

        predicate(new TestModel { Guid = a }).Should().BeTrue();
        predicate(new TestModel { Guid = b }).Should().BeTrue();
        predicate(new TestModel { Guid = Guid.NewGuid() }).Should().BeFalse();
        predicate(new TestModel { Guid = null }).Should().BeFalse();
    }

    [Fact]
    public void ModelsByGuid_returns_null_filter_when_guids_is_null()
    {
        var filter = new ModelsByGuid<TestModel>(null!);

        filter.Filter().Should().BeNull();
    }

    [Fact]
    public void ModelsByGuid_with_empty_set_matches_nothing()
    {
        var filter = new ModelsByGuid<TestModel>(Enumerable.Empty<Guid>()).Filter();
        filter.Should().NotBeNull();
        var predicate = filter!.Compile();

        predicate(new TestModel { Guid = Guid.NewGuid() }).Should().BeFalse();
    }
}
