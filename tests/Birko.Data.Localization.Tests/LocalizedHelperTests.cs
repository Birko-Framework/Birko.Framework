using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Localization.Expressions;
using Birko.Data.Localization.Models;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Localization.Tests;

/// <summary>
/// CR-L140: direct unit coverage for LocalizedOrderByHelper.ApplyInMemoryOrderBy / ApplyInMemoryPaging
/// (multi-field ASC/DESC + offset/limit) and LocalizedFilterHelper.BuildGuidFilter (empty/non-empty) +
/// CombineFilters (parameter rebinding). CR-L138: a sort on a non-comparable property degrades gracefully
/// instead of throwing.
/// </summary>
public class LocalizedHelperTests
{
    private sealed class SortModel : AbstractModel, ILocalizable
    {
        public string Name { get; set; } = string.Empty;
        public int Rank { get; set; }
        public object? Blob { get; set; }
        public IReadOnlyList<string> GetLocalizableFields() => new[] { nameof(Name) };
    }

    // ---- LocalizedOrderByHelper ----------------------------------------------

    [Fact]
    public void ApplyInMemoryOrderBy_multi_field_ascending_then_descending()
    {
        var items = new List<SortModel>
        {
            new() { Name = "b", Rank = 1 },
            new() { Name = "a", Rank = 2 },
            new() { Name = "a", Rank = 1 },
            new() { Name = "b", Rank = 2 },
        };

        // Order by Name ASC, then Rank DESC.
        var orderBy = OrderBy<SortModel>.By(x => x.Name).ThenByDescending(x => x.Rank);
        var sorted = LocalizedOrderByHelper.ApplyInMemoryOrderBy(items, orderBy);

        sorted.Select(x => (x.Name, x.Rank)).Should().ContainInOrder(
            ("a", 2), ("a", 1), ("b", 2), ("b", 1));
    }

    [Fact]
    public void ApplyInMemoryPaging_applies_offset_and_limit()
    {
        var items = Enumerable.Range(0, 5).Select(i => new SortModel { Rank = i }).ToList();

        var page = LocalizedOrderByHelper.ApplyInMemoryPaging(items, offset: 1, limit: 2);

        page.Select(x => x.Rank).Should().Equal(1, 2);
    }

    [Fact]
    public void ApplyInMemoryOrderBy_on_non_comparable_property_does_not_throw()
    {
        // Blob holds a plain object (not IComparable); the default comparer would throw at sort time.
        var items = new List<SortModel>
        {
            new() { Name = "a", Blob = new object() },
            new() { Name = "b", Blob = new object() },
        };

        var orderBy = OrderBy<SortModel>.By(x => x.Blob!);
        Action act = () => LocalizedOrderByHelper.ApplyInMemoryOrderBy(items, orderBy);

        act.Should().NotThrow();
    }

    // ---- LocalizedFilterHelper -----------------------------------------------

    [Fact]
    public void BuildGuidFilter_empty_set_matches_nothing()
    {
        var filter = LocalizedFilterHelper.BuildGuidFilter<SortModel>(new HashSet<Guid>()).Compile();

        filter(new SortModel { Guid = Guid.NewGuid() }).Should().BeFalse();
    }

    [Fact]
    public void BuildGuidFilter_non_empty_matches_only_members()
    {
        var inSet = Guid.NewGuid();
        var notInSet = Guid.NewGuid();
        var filter = LocalizedFilterHelper.BuildGuidFilter<SortModel>(new HashSet<Guid> { inSet }).Compile();

        filter(new SortModel { Guid = inSet }).Should().BeTrue();
        filter(new SortModel { Guid = notInSet }).Should().BeFalse();
        filter(new SortModel { Guid = null }).Should().BeFalse("the filter guards x.Guid != null");
    }

    [Fact]
    public void CombineFilters_rebinds_the_right_parameter_and_ands()
    {
        System.Linq.Expressions.Expression<Func<SortModel, bool>> left = x => x.Name == "a";
        System.Linq.Expressions.Expression<Func<SortModel, bool>> right = y => y.Rank == 1;

        var combined = LocalizedFilterHelper.CombineFilters(left, right).Compile();

        combined(new SortModel { Name = "a", Rank = 1 }).Should().BeTrue();
        combined(new SortModel { Name = "a", Rank = 2 }).Should().BeFalse();
        combined(new SortModel { Name = "b", Rank = 1 }).Should().BeFalse();
    }
}
