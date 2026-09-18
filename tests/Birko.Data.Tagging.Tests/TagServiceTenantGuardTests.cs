using System;
using System.Threading.Tasks;
using Birko.Data.Tagging;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Tagging.Tests;

/// <summary>
/// SH-H019 (TASK-126) — `TagServiceBase` stated its tenant-scoping contract in a comment and enforced
/// nothing.
///
/// <para>The 12 abstract data-access hooks take no tenant parameter, so the base class depended entirely
/// on every implementor remembering to filter. One implementation omitting one filter in one hook exposed
/// cross-tenant reads, cross-tenant <b>writes</b> and a cascade delete — because `GetTagAsync`,
/// `UpdateTagAsync`, `DeleteTagAsync` and `GetEntityTagsAsync` all reach their target through the same
/// `GetTagByIdAsync`, and `DeleteTagAsync` then calls `DeleteAllEntityTagsForTagAsync`.</para>
///
/// <para>These tests use a deliberately unscoped implementation — `SeedForeignTag` puts another tenant's
/// row where a missing filter would leave it — and assert the base catches it. That is the only shape
/// available: the framework ships no implementation of `TagServiceBase`, so this is a contract every
/// future implementor must satisfy rather than a leak reproducible against shipped code.</para>
/// </summary>
public class TagServiceTenantGuardTests
{
    private static (InMemoryTagService svc, Tag foreign) WithForeignTag()
    {
        var svc = new InMemoryTagService();
        var foreign = new Tag
        {
            Guid = Guid.NewGuid(),
            TenantGuid = Guid.NewGuid(), // NOT svc.Tenant — the row a missing filter would return
            Name = "theirs",
        };
        svc.SeedForeignTag(foreign);
        return (svc, foreign);
    }

    // ── By-identity: throws, never a silent wrong answer ────────────────────────────────────────

    [Fact]
    public async Task GetTag_throws_rather_than_returning_another_tenants_tag()
    {
        var (svc, foreign) = WithForeignTag();

        var act = () => svc.GetTagAsync(foreign.Guid!.Value);

        (await act.Should().ThrowAsync<CrossTenantTagAccessException>())
            .Which.RecordTenant.Should().Be(foreign.TenantGuid);
    }

    [Fact]
    public async Task UpdateTag_throws_rather_than_writing_to_another_tenants_tag()
    {
        // The write half. Before the guard this renamed another tenant's tag.
        var (svc, foreign) = WithForeignTag();

        var act = () => svc.UpdateTagAsync(foreign.Guid!.Value, name: "renamed");

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        foreign.Name.Should().Be("theirs", "the foreign tag must be untouched");
    }

    [Fact]
    public async Task DeleteTag_throws_and_never_reaches_the_entity_link_cascade()
    {
        // The worst path: DeleteTagAsync calls DeleteAllEntityTagsForTagAsync BEFORE deleting the tag,
        // so an unguarded foreign delete destroyed another tenant's links too. The guard runs first.
        var (svc, foreign) = WithForeignTag();

        var act = () => svc.DeleteTagAsync(foreign.Guid!.Value);

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        svc.TagCount.Should().Be(1, "the foreign tag is still there");
        svc.DeleteAllEntityTagsCalls.Should().Be(0, "the cascade must not have been reached");
    }

    [Fact]
    public async Task CreateTag_throws_rather_than_handing_back_another_tenants_tag_as_newly_created()
    {
        // CreateTagAsync returns FindTagByNameAsync's hit INSTEAD of inserting, so an unscoped by-name
        // hook silently gives the caller a foreign tag they believe they just created.
        var (svc, _) = WithForeignTag();

        var act = () => svc.CreateTagAsync("theirs");

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        svc.CreateTagCalls.Should().Be(0);
    }

    [Fact]
    public async Task AttachTagByName_throws_rather_than_linking_a_foreign_tag_to_a_local_entity()
    {
        var (svc, _) = WithForeignTag();

        var act = () => svc.AttachTagByNameAsync("Doc", Guid.NewGuid(), "theirs");

        await act.Should().ThrowAsync<CrossTenantTagAccessException>();
        svc.LinkCount.Should().Be(0);
    }

    // ── Collections: filtered, so one leaked row cannot blank a list ────────────────────────────

    [Fact]
    public async Task List_and_search_drop_foreign_tags_instead_of_throwing()
    {
        // Deliberately different from the by-identity rule, and the reason is the failure mode: throwing
        // here would take out an entire tag picker because one row leaked.
        var (svc, _) = WithForeignTag();
        svc.SeedTag(new Tag { Name = "mine" });

        var listed = await svc.ListTagsAsync();
        listed.Should().ContainSingle().Which.Name.Should().Be("mine");

        var found = await svc.SearchTagsAsync("the");
        found.Should().BeEmpty("the only match belongs to another tenant");
    }

    // ── Guid.Empty is a tenant, not a wildcard ──────────────────────────────────────────────────

    [Fact]
    public async Task An_empty_tenant_on_a_record_is_a_value_not_a_wildcard()
    {
        // Decided explicitly (criterion 4). A tag stamped Guid.Empty is NOT visible to a real tenant —
        // treating empty as "matches everything" is how a wrapper elsewhere in this family once returned
        // every tenant's rows to an unconfigured scope.
        var svc = new InMemoryTagService();
        svc.SeedForeignTag(new Tag { Guid = Guid.NewGuid(), TenantGuid = Guid.Empty, Name = "unstamped" });

        (await svc.ListTagsAsync()).Should().BeEmpty(
            "Guid.Empty is a distinct tenant value, so it does not match a real ambient tenant");
    }

    // ── A correctly-scoped implementation is unaffected ─────────────────────────────────────────

    [Fact]
    public async Task A_correctly_scoped_implementation_sees_no_behaviour_change()
    {
        // Criterion 5, and the bidirectional half of every assertion above: the guard must remove the
        // leak without narrowing anything legitimate.
        var svc = new InMemoryTagService();
        var dto = await svc.CreateTagAsync("mine");

        (await svc.GetTagAsync(dto.Id)).Should().NotBeNull();
        (await svc.ListTagsAsync()).Should().ContainSingle();
        (await svc.SearchTagsAsync("min")).Should().ContainSingle();

        await svc.UpdateTagAsync(dto.Id, name: "renamed");
        (await svc.GetTagAsync(dto.Id))!.Name.Should().Be("renamed");

        var entity = Guid.NewGuid();
        await svc.AttachTagAsync("Doc", entity, dto.Id);
        (await svc.GetEntityTagsAsync("Doc", entity)).Should().ContainSingle();
        (await svc.GetEntityTagsBatchAsync("Doc", new[] { entity }))[entity].Should().ContainSingle();

        await svc.DeleteTagAsync(dto.Id);
        svc.TagCount.Should().Be(0);
    }
}
