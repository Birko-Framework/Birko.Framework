using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for one-shot <see cref="AuthorizationCode"/> records.
/// <para>
/// CR-L351 / TASK-518: single use is the store's job, through <see cref="TryRedeemAsync"/>. The token endpoint used
/// to read the code, check <c>Used</c> and write it back, so two concurrent redemptions of one code could both pass the
/// check and both be issued tokens. A store that can make the claim atomic (a conditional update on SQL, a lock in
/// memory) overrides <see cref="TryRedeemAsync"/>; the default keeps that read-check-write window and is only safe
/// for a single caller.
/// </para>
/// </summary>
public interface IAuthorizationCodeStore : IAsyncStore<AuthorizationCode>
{
    Task<AuthorizationCode?> GetByCodeAsync(string code, CancellationToken ct = default)
        => ReadAsync(c => c.Code == code, ct);

    /// <summary>
    /// Marks the code used if, and only if, it is not used yet. Returns false when it already was (or no longer
    /// exists) — the caller lost the race and must not issue tokens. On true, <paramref name="code"/>.Used is set.
    /// </summary>
    /// <remarks>
    /// The default re-reads the stored code by its Guid and writes it back: NOT atomic. Override it with a
    /// conditional update wherever the store can express one.
    /// </remarks>
    async Task<bool> TryRedeemAsync(AuthorizationCode code, CancellationToken ct = default)
    {
        if (code.Guid is not { } guid) return false;
        var current = await ReadAsync(guid, ct).ConfigureAwait(false);
        if (current == null || current.Used) return false;
        current.Used = true;
        await UpdateAsync(current, ct: ct).ConfigureAwait(false);
        code.Used = true;
        return true;
    }
}
