using Birko.Data.Sync.CosmosDB.Models;
using System;
using System.Linq;

namespace Birko.Data.Sync.CosmosDB.Stores;

/// <summary>
/// The one producer of the scope/tenant predicate that every Cosmos sync-knowledge query is built on.
/// </summary>
/// <remarks>
/// <para><b>SH-H013</b> (TASK-309): the four query sites — <c>GetKnowledge</c> and <c>GetKnowledgeItem</c> on
/// both the sync and async stores — each wrote <c>x.Scope == scope &amp;&amp; x.TenantId == tenantId</c>
/// inline. For a <b>null</b> tenant that renders as <c>root["TenantId"] = null</c>, and in the Cosmos SQL
/// dialect a comparison against null is <b>Undefined</b>, not true — so the query matched <b>nothing</b>,
/// including the documents whose <c>TenantId</c> genuinely is null (the model declares it <c>Guid?</c>).
/// Knowledge was then never found, never updated and never deleted, and every run looked like an initial
/// sync. Measured offline against Microsoft.Azure.Cosmos 3.63.0:
/// <c>SELECT VALUE root FROM root WHERE ((root["Scope"] = "s") AND (root["TenantId"] = null))</c>.</para>
/// <para>A null <c>tenantId</c> means <b>do not filter by tenant</b>. That is not a guess:
/// <c>TenantSyncProvider.ResolveTenantScope</c> returns null in exactly two cases — an explicit
/// <c>ITenantContext.IsAllTenantsScope</c>, and an entity type with no <c>TenantGuid</c> property — and both
/// mean every row in the scope. It is also what <c>Birko.Data.Sync.RavenDB</c> has always done, so the two
/// backends now agree; the RavenDB half of SH-H013 was <b>refuted</b> for that reason.</para>
/// <para>It lives here rather than inline so the rule has one statement and four callers, and so it can be
/// rendered to SQL offline and asserted — the Cosmos LINQ provider emits query text without a live account.</para>
/// </remarks>
internal static class CosmosSyncKnowledgeQuery
{
    /// <summary>
    /// Narrows <paramref name="source"/> to one sync scope, and to one tenant only when a tenant was given.
    /// </summary>
    internal static IQueryable<CosmosSyncKnowledgeItem> ApplyScope(
        IQueryable<CosmosSyncKnowledgeItem> source,
        string scope,
        Guid? tenantId)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var scoped = source.Where(x => x.Scope == scope);
        return tenantId.HasValue
            ? scoped.Where(x => x.TenantId == tenantId.Value)
            : scoped;
    }
}
