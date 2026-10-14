using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Birko.Data.ElasticSearch.Tests;

public class SortModel : AbstractModel
{
    public DateTime At { get; set; }
    public int Rank { get; set; }
    public string? Name { get; set; }

    [Keyword(Name = "code")]
    public string Code { get; set; } = string.Empty;

    [Text(Name = "notes", Index = false)]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// TASK-615 — a sort names the field the document is stored under. It sent <c>OrderByField.PropertyName</c>
/// verbatim (<c>UpdatedAt</c>) while NEST writes <c>updatedAt</c>, so every <c>orderBy</c> was a 400
/// "No mapping found for [UpdatedAt] in order to sort on" — every workflow <c>Find*</c> and job dequeue threw.
/// </summary>
public class SortFieldNameTests
{
    private static string[] Fields(OrderBy<SortModel> orderBy)
        => Birko.Data.ElasticSearch.ElasticSearch.Sorts(orderBy).Cast<FieldSort>().Select(s => s.Field.Name).ToArray();

    [Fact]
    public void A_sort_names_the_stored_field_with_keyword_where_the_string_is_dynamically_mapped()
        => Fields(OrderBy<SortModel>.By(x => x.At).ThenBy(x => x.Rank).ThenBy(x => x.Name).ThenByDescending(x => x.Code))
            .Should().Equal("at", "rank", "name.keyword", "code");

    [Fact]
    public void A_sort_by_name_accepts_the_property_or_the_stored_field_name()
    {
        Fields(OrderBy<SortModel>.ByName("Rank")).Should().Equal("rank");
        Fields(OrderBy<SortModel>.ByName("code")).Should().Equal("code");
    }

    [Fact]
    public void A_sort_on_a_member_the_type_does_not_have_is_refused()
        => ((Action)(() => Fields(OrderBy<SortModel>.ByName("Nope")))).Should().Throw<ArgumentException>().WithMessage("*Nope*SortModel*");

    [Fact]
    public void A_sort_on_a_text_mapped_member_is_refused()
        => ((Action)(() => Fields(OrderBy<SortModel>.By(x => x.Notes)))).Should().Throw<NotSupportedException>().WithMessage("*Notes*");
}

/// <summary>TASK-615, live: ascending and descending sorts return rows in order on the sync and async stores.</summary>
public sealed class SortFieldNameLiveTests : IDisposable
{
    private readonly ElasticLive _live;
    public SortFieldNameLiveTests(ITestOutputHelper output) => _live = new ElasticLive(output);
    public void Dispose() => _live.Dispose();

    private static readonly SortModel[] Rows =
    {
        new() { At = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Rank = 3, Name = "b", Code = "Y" },
        new() { At = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), Rank = 1, Name = "c", Code = "Z" },
        new() { At = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), Rank = 2, Name = "a", Code = "X" },
    };

    [Fact]
    public void The_sync_store_sorts_by_date_int_and_both_kinds_of_string()
    {
        if (!_live.Ready()) return;
        var store = new ElasticSearchStore<SortModel>();
        store.SetSettings(_live.NewSettings());
        foreach (var row in Rows) store.Create(new SortModel { At = row.At, Rank = row.Rank, Name = row.Name, Code = row.Code });
        ElasticLive.Refresh();

        store.Read(null, OrderBy<SortModel>.By(x => x.At)).Select(x => x.Rank).Should().Equal(1, 3, 2);
        store.Read(null, OrderBy<SortModel>.ByDescending(x => x.Rank)).Select(x => x.Rank).Should().Equal(3, 2, 1);
        store.Read(null, OrderBy<SortModel>.By(x => x.Name)).Select(x => x.Name).Should().Equal("a", "b", "c");
        store.Read(null, OrderBy<SortModel>.ByDescending(x => x.Code)).Select(x => x.Code).Should().Equal("Z", "Y", "X");
    }

    [Fact]
    public async Task The_async_store_sorts_by_date_int_and_both_kinds_of_string()
    {
        if (!_live.Ready()) return;
        var store = new AsyncElasticSearchStore<SortModel>();
        store.SetSettings(_live.NewSettings());
        foreach (var row in Rows) await store.CreateAsync(new SortModel { At = row.At, Rank = row.Rank, Name = row.Name, Code = row.Code });
        ElasticLive.Refresh();

        (await store.ReadAsync(null, OrderBy<SortModel>.ByDescending(x => x.At))).Select(x => x.Rank).Should().Equal(2, 3, 1);
        (await store.ReadAsync(null, OrderBy<SortModel>.By(x => x.Rank))).Select(x => x.Rank).Should().Equal(1, 2, 3);
        (await store.ReadAsync(null, OrderBy<SortModel>.ByDescending(x => x.Name))).Select(x => x.Name).Should().Equal("c", "b", "a");
        (await store.ReadAsync(null, OrderBy<SortModel>.By(x => x.Code))).Select(x => x.Code).Should().Equal("X", "Y", "Z");
    }
}
