using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tagging.Tests;

/// <summary>
/// CR-H107: TagServiceBase shipped with no tests. These cover the shared logic via an in-memory
/// subclass: name dedup, update null/whitespace handling, delete cascade, attach idempotency,
/// set-tags diffing, attach-by-name create-vs-find, batch grouping + empty backfill, and failure paths.
/// </summary>
public class TagServiceBaseTests
{
    [Fact]
    public async Task CreateTag_DedupsByName()
    {
        var svc = new InMemoryTagService();
        var first = await svc.CreateTagAsync("Urgent");
        var second = await svc.CreateTagAsync(" Urgent "); // trimmed → same name

        second.Id.Should().Be(first.Id);
        svc.CreateTagCalls.Should().Be(1, "the second call matches an existing tag by name");
        svc.TagCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdateTag_BlankColorAndGroup_BecomeNull()
    {
        var svc = new InMemoryTagService();
        var tag = await svc.CreateTagAsync("T", color: "red", group: "g");

        await svc.UpdateTagAsync(tag.Id, name: "  T2  ", color: "   ", group: "");

        var updated = await svc.GetTagAsync(tag.Id);
        updated!.Name.Should().Be("T2");        // trimmed
        updated.Color.Should().BeNull();        // whitespace → null
        updated.TagGroup.Should().BeNull();     // empty → null
    }

    [Fact]
    public async Task UpdateTag_MissingTag_Throws()
    {
        var svc = new InMemoryTagService();
        await svc.Invoking(s => s.UpdateTagAsync(Guid.NewGuid(), name: "x"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteTag_MissingTag_IsNoOp()
    {
        var svc = new InMemoryTagService();
        await svc.Invoking(s => s.DeleteTagAsync(Guid.NewGuid())).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteTag_CascadesEntityLinks()
    {
        var svc = new InMemoryTagService();
        var tag = await svc.CreateTagAsync("T");
        var entity = Guid.NewGuid();
        await svc.AttachTagAsync("Doc", entity, tag.Id);
        svc.LinkCount.Should().Be(1);

        await svc.DeleteTagAsync(tag.Id);

        svc.LinkCount.Should().Be(0, "deleting a tag removes its entity links");
        svc.TagCount.Should().Be(0);
    }

    [Fact]
    public async Task AttachTag_IsIdempotent()
    {
        var svc = new InMemoryTagService();
        var tag = await svc.CreateTagAsync("T");
        var entity = Guid.NewGuid();

        await svc.AttachTagAsync("Doc", entity, tag.Id);
        await svc.AttachTagAsync("Doc", entity, tag.Id);

        svc.LinkCount.Should().Be(1);
    }

    [Fact]
    public async Task SetEntityTags_AddsAndRemovesToMatchDesired()
    {
        var svc = new InMemoryTagService();
        var a = await svc.CreateTagAsync("A");
        var b = await svc.CreateTagAsync("B");
        var c = await svc.CreateTagAsync("C");
        var entity = Guid.NewGuid();

        await svc.SetEntityTagsAsync("Doc", entity, new[] { a.Id, b.Id });
        await svc.SetEntityTagsAsync("Doc", entity, new[] { b.Id, c.Id }); // drop A, keep B, add C

        var tags = await svc.GetEntityTagsAsync("Doc", entity);
        tags.Select(t => t.Id).Should().BeEquivalentTo(new[] { b.Id, c.Id });
    }

    [Fact]
    public async Task AttachTagByName_CreatesWhenMissing_FindsWhenExisting()
    {
        var svc = new InMemoryTagService();
        var entity = Guid.NewGuid();

        var created = await svc.AttachTagByNameAsync("Doc", entity, "New");
        svc.TagCount.Should().Be(1);

        var found = await svc.AttachTagByNameAsync("Doc", Guid.NewGuid(), "New");
        found.Id.Should().Be(created.Id, "an existing tag name is reused, not recreated");
        svc.TagCount.Should().Be(1);
    }

    [Fact]
    public async Task GetEntityTagsBatch_GroupsAndBackfillsEmpty()
    {
        var svc = new InMemoryTagService();
        var tag = await svc.CreateTagAsync("T");
        var withTag = Guid.NewGuid();
        var withoutTag = Guid.NewGuid();
        await svc.AttachTagAsync("Doc", withTag, tag.Id);

        var batch = await svc.GetEntityTagsBatchAsync("Doc", new[] { withTag, withoutTag });

        batch[withTag].Should().ContainSingle().Which.Id.Should().Be(tag.Id);
        batch[withoutTag].Should().BeEmpty("entities with no tags get an empty list");
    }

    [Fact]
    public async Task GetEntityTagsBatch_EmptyInput_ReturnsEmpty()
    {
        var svc = new InMemoryTagService();
        (await svc.GetEntityTagsBatchAsync("Doc", Array.Empty<Guid>())).Should().BeEmpty();
    }
}
