using Birko.Data.Sync.CosmosDB.Models;
using Birko.Data.Sync.CosmosDB.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Birko.Data.Sync.CosmosDB.Tests;

/// <summary>
/// SH-H013 (TASK-309): every Cosmos sync-knowledge query filtered with an unconditional
/// <c>x.TenantId == tenantId</c>. For a <b>null</b> tenant the provider renders that as
/// <c>root["TenantId"] = null</c>, and in the Cosmos SQL dialect a comparison against null is
/// <b>Undefined</b>, not true — so the query matched <b>nothing</b>, including documents whose
/// <c>TenantId</c> genuinely is null. Knowledge was never found, never updated and never deleted, and every
/// run looked like an initial sync.
/// <para>A null tenant means "do not filter by tenant": <c>TenantSyncProvider.ResolveTenantScope</c> returns
/// null only for an explicit all-tenants scope or for an entity with no tenant property, and both mean every
/// row in the scope. <c>Birko.Data.Sync.RavenDB</c> has always done this, which is why the RavenDB half of
/// SH-H013 was <b>refuted</b> — the two backends now agree rather than diverging.</para>
/// <para>⚠ <c>CosmosSyncTenantScopingTests</c> states that "the LINQ filter itself needs a live Cosmos DB".
/// Measured against Microsoft.Azure.Cosmos 3.63.0, that is <b>not</b> so for the filter's <i>text</i>: the
/// LINQ provider renders query SQL with no account and no network. Only <i>executing</i> it needs a server.
/// These tests therefore assert the emitted SQL, which is the exact artefact the defect lived in.</para>
/// </summary>
public class CosmosSyncKnowledgeQueryTests
{
    /// <summary>
    /// A container built from a well-formed but unreachable connection string. Nothing here performs I/O —
    /// <c>GetContainer</c> is a proxy and <c>ToQueryDefinition()</c> renders locally.
    /// </summary>
    private static Container OfflineContainer()
    {
        const string connectionString =
            "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM"
            + "+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";
        return new CosmosClient(connectionString).GetContainer("db", "SyncKnowledge");
    }

    private static string RenderedSql(Guid? tenantId)
    {
        var source = OfflineContainer().GetItemLinqQueryable<CosmosSyncKnowledgeItem>();
        return CosmosSyncKnowledgeQuery.ApplyScope(source, "Default", tenantId)
            .ToQueryDefinition().QueryText;
    }

    [Fact]
    public void A_null_tenant_emits_no_TenantId_term_at_all()
    {
        var sql = RenderedSql(null);

        // The defect rendered `(root["TenantId"] = null)`. Asserting the absence of the *column* is what
        // catches it however the provider chooses to spell the comparison.
        sql.Should().NotContain("TenantId");
        sql.Should().Contain("Scope");
    }

    [Fact]
    public void A_null_tenant_never_emits_a_comparison_against_null()
    {
        RenderedSql(null).Should().NotContain("= null",
            "a Cosmos SQL comparison against null is Undefined, so such a predicate matches no document");
    }

    [Fact]
    public void A_supplied_tenant_still_narrows_the_query()
    {
        // The other side of the switch. Without this, "drop the tenant term" would pass the two tests
        // above while silently removing tenant isolation from every tenant-scoped run.
        var tenant = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var sql = RenderedSql(tenant);

        sql.Should().Contain("TenantId");
        sql.Should().Contain(tenant.ToString());
        sql.Should().Contain("Scope");
    }

    // ------------------------------------------------------------- semantics

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static List<CosmosSyncKnowledgeItem> Rows() =>
    [
        new() { Scope = "Default", TenantId = TenantA, EntityGuid = Guid.NewGuid() },
        new() { Scope = "Default", TenantId = TenantB, EntityGuid = Guid.NewGuid() },
        new() { Scope = "Default", TenantId = null, EntityGuid = Guid.NewGuid() },
        new() { Scope = "Other", TenantId = TenantA, EntityGuid = Guid.NewGuid() },
    ];

    [Fact]
    public void A_null_tenant_selects_every_row_in_the_scope()
    {
        var selected = CosmosSyncKnowledgeQuery
            .ApplyScope(Rows().AsQueryable(), "Default", null)
            .ToList();

        // ⚠ In-memory LINQ UNDERSTATES the defect: `null == null` is true there, so the old predicate
        // returned the one untenanted row. Against a real account it returned nothing at all, because
        // Cosmos SQL answers Undefined. Either way this assertion separates old from new.
        selected.Should().HaveCount(3);
        selected.Should().OnlyContain(x => x.Scope == "Default");
    }

    [Fact]
    public void A_supplied_tenant_selects_only_that_tenants_rows_in_the_scope()
    {
        var selected = CosmosSyncKnowledgeQuery
            .ApplyScope(Rows().AsQueryable(), "Default", TenantA)
            .ToList();

        selected.Should().ContainSingle().Which.TenantId.Should().Be(TenantA);
    }

    [Fact]
    public void The_scope_term_is_always_applied()
    {
        CosmosSyncKnowledgeQuery.ApplyScope(Rows().AsQueryable(), "Other", null)
            .Should().ContainSingle().Which.Scope.Should().Be("Other");
    }
}
