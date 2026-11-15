using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Tagging;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tagging.Tests;

/// <summary>
/// TASK-147 (the user's decision, 2026-11-15): attaching refuses a tag that belongs to another tenant
/// (<see cref="CrossTenantTagAccessException"/>, as the read paths do) and a tag that does not exist, so a bad link
/// can no longer be stored. The cost is pinned: <c>AttachTagAsync</c> loads its one tag, and
/// <c>SetEntityTagsAsync</c> lists the tenant's tags ONCE per call however many tags it adds, so CR-M172's removal of
/// a per-tag query is not undone.
/// </summary>
public class TagServiceAttachValidationTests
{
    private static Tag Own(InMemoryTagService svc, string name)
    {
        var t = new Tag { Name = name };
        svc.SeedTag(t);
        return t;
    }

    private static Tag Foreign(InMemoryTagService svc, string name)
    {
        var t = new Tag { Name = name, TenantGuid = Guid.NewGuid() };
        svc.SeedForeignTag(t);
        return t;
    }

    [Fact]
    public async Task AttachTag_links_an_owned_tag_with_one_tag_lookup()
    {
        var svc = new InMemoryTagService();
        var tag = Own(svc, "mine");
        var entity = Guid.NewGuid();

        await svc.AttachTagAsync("Doc", entity, tag.Guid!.Value);

        svc.LinkCount.Should().Be(1);
        svc.GetTagByIdCalls.Should().Be(1);
    }

    [Fact]
    public async Task AttachTag_refuses_another_tenants_tag_and_stores_nothing()
    {
        var svc = new InMemoryTagService();
        var theirs = Foreign(svc, "theirs");

        var act = () => svc.AttachTagAsync("Doc", Guid.NewGuid(), theirs.Guid!.Value);

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        svc.LinkCount.Should().Be(0);
    }

    [Fact]
    public async Task AttachTag_refuses_a_tag_that_does_not_exist_and_stores_nothing()
    {
        var svc = new InMemoryTagService();
        var missing = Guid.NewGuid();

        var act = () => svc.AttachTagAsync("Doc", Guid.NewGuid(), missing);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{missing}*not found*");
        svc.LinkCount.Should().Be(0);
    }

    [Fact]
    public async Task AttachTagByName_does_not_load_the_tag_a_second_time()
    {
        var svc = new InMemoryTagService();
        Own(svc, "mine");

        await svc.AttachTagByNameAsync("Doc", Guid.NewGuid(), "mine");

        svc.LinkCount.Should().Be(1);
        svc.GetTagByIdCalls.Should().Be(0, "the by-name lookup already proved the tag is this tenant's");
    }

    [Fact]
    public async Task SetEntityTags_validates_every_new_tag_with_one_listing_not_one_lookup_per_tag()
    {
        var svc = new InMemoryTagService();
        var ids = Enumerable.Range(0, 5).Select(i => Own(svc, $"t{i}").Guid!.Value).ToList();
        var entity = Guid.NewGuid();

        await svc.SetEntityTagsAsync("Doc", entity, ids);

        svc.LinkCount.Should().Be(5);
        svc.ListAllTagsCalls.Should().Be(1, "one listing per call, however many tags are added");
        svc.GetTagByIdCalls.Should().Be(0);
        svc.GetEntityTagLinksCalls.Should().Be(1, "CR-M172: no per-tag link re-query either");
    }

    [Fact]
    public async Task SetEntityTags_that_only_removes_lists_nothing()
    {
        var svc = new InMemoryTagService();
        var a = Own(svc, "a");
        var entity = Guid.NewGuid();
        await svc.AttachTagAsync("Doc", entity, a.Guid!.Value);
        var listingsBefore = svc.ListAllTagsCalls;

        await svc.SetEntityTagsAsync("Doc", entity, Array.Empty<Guid>());

        svc.LinkCount.Should().Be(0);
        svc.ListAllTagsCalls.Should().Be(listingsBefore);
    }

    [Fact]
    public async Task SetEntityTags_refuses_a_foreign_tag_before_changing_anything()
    {
        var svc = new InMemoryTagService();
        var kept = Own(svc, "kept");
        var dropped = Own(svc, "dropped");
        var added = Own(svc, "added");
        var theirs = Foreign(svc, "theirs");
        var entity = Guid.NewGuid();
        await svc.SetEntityTagsAsync("Doc", entity, new[] { kept.Guid!.Value, dropped.Guid!.Value });

        var act = () => svc.SetEntityTagsAsync("Doc", entity, new[] { kept.Guid!.Value, added.Guid!.Value, theirs.Guid!.Value });

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        (await svc.GetEntityTagsAsync("Doc", entity)).Select(t => t.Name).Should().BeEquivalentTo(new[] { "kept", "dropped" },
            "a refused call removes nothing and adds nothing");
    }

    [Fact]
    public async Task SetEntityTags_refuses_a_tag_that_does_not_exist_before_changing_anything()
    {
        var svc = new InMemoryTagService();
        var kept = Own(svc, "kept");
        var entity = Guid.NewGuid();
        await svc.SetEntityTagsAsync("Doc", entity, new[] { kept.Guid!.Value });
        var missing = Guid.NewGuid();

        var act = () => svc.SetEntityTagsAsync("Doc", entity, new[] { missing });

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage($"*{missing}*not found*");
        (await svc.GetEntityTagsAsync("Doc", entity)).Select(t => t.Name).Should().BeEquivalentTo(new[] { "kept" });
    }
}
