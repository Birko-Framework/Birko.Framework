using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Patterns.IndexManagement;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.IndexManagement;
using Birko.Security.Grants.SQL.Mappings;
using Birko.Security.Grants.SQL.Stores;

namespace Birko.Security.Grants.SQL;

/// <summary>
/// Creates the indexes the grant lookups read by. They are created through the provider's <see cref="IIndexManager"/> rather than
/// declared with the fluent <c>HasIndex</c>, which <c>ModelMapRegistry.ApplyToDatabase</c> does not emit and no
/// migration reads (measured 2026-10-14, TASK-594). A declaration nothing applies would leave every grant check a
/// table scan while the mapping claimed otherwise.
/// </summary>
public static class GrantSqlIndexes
{
    /// <summary>(UserGuid, ResourceType): every <c>CanAsync</c> and <c>ListResourcesAsync</c>.</summary>
    public const string GrantsByUser = "IX_BirkoResourceGrants_User";

    /// <summary>(ResourceType, ResourceId): <c>ListGranteesAsync</c> and the last-owner count.</summary>
    public const string GrantsByResource = "IX_BirkoResourceGrants_Resource";

    /// <summary>(Email): pending invitations at sign-in.</summary>
    public const string InvitationsByEmail = "IX_BirkoInvitations_Email";

    /// <summary>
    /// Ensures both tables exist and every lookup index is on them. Idempotent: an index already there is left alone.
    /// Call once at startup, after <see cref="GrantSqlMappings.Register"/> and <c>ApplyToDatabase()</c>.
    /// </summary>
    /// <param name="indexManager">
    /// The provider's own index manager: <c>c =&gt; new SqLiteIndexManager(c)</c>, <c>new PostgreSqlIndexManager(c)</c>,
    /// <c>new MSSqlIndexManager(c)</c>, <c>new MySqlIndexManager(c)</c>. Required, with no default: the base
    /// <see cref="SqlIndexManager"/> speaks MySQL's <c>information_schema</c> and fails on every other provider
    /// (measured on SQLite, TASK-594).
    /// </param>
    public static async Task EnsureAsync<DB>(SqlResourceGrantStore<DB> grants, SqlInvitationStore<DB> invitations,
        Func<AbstractConnectorBase, IIndexManager> indexManager, CancellationToken ct = default)
        where DB : AbstractConnector
    {
        if (grants == null) throw new ArgumentNullException(nameof(grants));
        if (invitations == null) throw new ArgumentNullException(nameof(invitations));
        if (indexManager == null) throw new ArgumentNullException(nameof(indexManager));

        await grants.InitAsync(ct).ConfigureAwait(false);
        await invitations.InitAsync(ct).ConfigureAwait(false);

        await EnsureOnAsync(grants.Connector, indexManager, ResourceGrantMapping.Table, new IndexDefinition[]
        {
            Index(GrantsByUser, "UserGuid", "ResourceType"),
            Index(GrantsByResource, "ResourceType", "ResourceId"),
        }, ct).ConfigureAwait(false);
        await EnsureOnAsync(invitations.Connector, indexManager, InvitationMapping.Table, new IndexDefinition[]
        {
            Index(InvitationsByEmail, "Email"),
        }, ct).ConfigureAwait(false);
    }

    private static async Task EnsureOnAsync(AbstractConnectorBase? connector, Func<AbstractConnectorBase, IIndexManager> indexManager,
        string table, IEnumerable<IndexDefinition> indexes, CancellationToken ct)
    {
        if (connector == null)
            throw new InvalidOperationException($"The store for '{table}' has no connector: call SetSettings(...) before ensuring its indexes.");

        var manager = indexManager(connector);
        foreach (var index in indexes)
        {
            if (!await manager.ExistsAsync(index.Name, table, ct).ConfigureAwait(false))
                await manager.CreateAsync(index, table, ct).ConfigureAwait(false);
        }
    }

    private static IndexDefinition Index(string name, params string[] columns)
    {
        var fields = new List<IndexField>();
        foreach (var column in columns)
            fields.Add(IndexField.Ascending(column));
        return new IndexDefinition { Name = name, Fields = fields };
    }
}
