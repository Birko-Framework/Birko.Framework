using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for one-shot <see cref="AuthorizationCode"/> records.
/// <para>
/// CR-L351: the token endpoint enforces single use with a read → check <c>Used</c> → set-and-
/// <see cref="IAsyncUpdateStore{T}.UpdateAsync"/> sequence. That sequence is only race-free if
/// <see cref="IAsyncUpdateStore{T}.UpdateAsync"/> (or a store-specific claim) is effectively an atomic
/// conditional update — otherwise two concurrent redemptions of the same code can both observe
/// <c>Used == false</c> and both succeed. The reference InMemory/JSON stores are last-write-wins and
/// carry this TOCTOU window; database backends should implement redemption as a conditional update
/// (e.g. <c>UPDATE ... SET Used = 1 WHERE Code = @c AND Used = 0</c>) so the losing request fails.
/// </para>
/// </summary>
public interface IAuthorizationCodeStore : IAsyncStore<AuthorizationCode>
{
    Task<AuthorizationCode?> GetByCodeAsync(string code, CancellationToken ct = default)
        => ReadAsync(c => c.Code == code, ct);
}
