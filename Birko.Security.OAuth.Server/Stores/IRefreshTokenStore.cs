using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for refresh-token records. The token presented by the client is hashed
/// before lookup — callers should pass <see cref="Models.RefreshTokenRecord.TokenHash"/>,
/// not the plaintext.
/// </summary>
public interface IRefreshTokenStore : IAsyncStore<RefreshTokenRecord>
{
    Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
        => ReadAsync(r => r.TokenHash == tokenHash, ct);
}
