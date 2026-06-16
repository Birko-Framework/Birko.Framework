using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for prior-consent records — used to skip the consent UI when a user
/// has already approved a (clientId, scopes) pair.
/// </summary>
public interface IConsentStore : IAsyncStore<ConsentRecord>
{
    Task<ConsentRecord?> GetAsync(string userId, string clientId, CancellationToken ct = default)
        => ReadAsync(c => c.UserId == userId && c.ClientId == clientId, ct);
}
