using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for one-shot <see cref="AuthorizationCode"/> records.
/// </summary>
public interface IAuthorizationCodeStore : IAsyncStore<AuthorizationCode>
{
    Task<AuthorizationCode?> GetByCodeAsync(string code, CancellationToken ct = default)
        => ReadAsync(c => c.Code == code, ct);
}
