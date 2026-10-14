using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Security.Grants.Models;
using Birko.Security.Grants.Stores;

namespace Birko.Security.Grants.SQL.Stores;

/// <summary>
/// <see cref="IResourceGrantStore"/> on any SQL provider: <c>SqlResourceGrantStore&lt;SqLiteConnector&gt;</c>,
/// <c>&lt;MSSqlConnector&gt;</c>, … The lookups are the interface's default methods; the provider's bulk store does the rest.
/// Register the mappings first (<see cref="GrantSqlMappings.Register"/>).
/// </summary>
public class SqlResourceGrantStore<DB> : AsyncDataBaseBulkStore<DB, ResourceGrant>, IResourceGrantStore
    where DB : AbstractConnector
{
}

/// <summary><see cref="IInvitationStore"/> on any SQL provider; see <see cref="SqlResourceGrantStore{DB}"/>.</summary>
public class SqlInvitationStore<DB> : AsyncDataBaseBulkStore<DB, Invitation>, IInvitationStore
    where DB : AbstractConnector
{
}
