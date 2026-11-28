using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for <see cref="OAuthClient"/> records.
/// Implementations may back this with any <see cref="IAsyncStore{T}"/>; the
/// default <see cref="GetByClientIdAsync"/> falls through to <see cref="IAsyncReadStore{T}.ReadAsync(System.Linq.Expressions.Expression{System.Func{T,bool}}?, CancellationToken)"/>.
/// </summary>
public interface IOAuthClientStore : IAsyncStore<OAuthClient>
{
    /// <summary>
    /// Looks up a client by its public <see cref="OAuthClient.ClientId"/>.
    /// Default implementation does a filtered <see cref="IAsyncReadStore{T}.ReadAsync(System.Linq.Expressions.Expression{System.Func{T,bool}}?, CancellationToken)"/>.
    /// </summary>
    Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
        => ReadAsync(c => c.ClientId == clientId, ct);

    /// <summary>
    /// Looks up a client by its <see cref="OAuthClient.Name"/> (TASK-1013), e.g. to keep two clients from sharing a
    /// name. Names are not unique by themselves: when several clients share one, any of them may be returned.
    /// Default implementation does a filtered <see cref="IAsyncReadStore{T}.ReadAsync(System.Linq.Expressions.Expression{System.Func{T,bool}}?, CancellationToken)"/>.
    /// </summary>
    Task<OAuthClient?> GetByNameAsync(string name, CancellationToken ct = default)
        => ReadAsync(c => c.Name == name, ct);
}
