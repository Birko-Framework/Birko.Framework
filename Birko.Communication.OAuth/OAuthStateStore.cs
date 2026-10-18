using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Communication.OAuth;

/// <summary>What a consumed sign-in <c>state</c> carried.</summary>
/// <param name="Data">The value stored with the state when it was created (e.g. a PKCE verifier or a return URL), or null.</param>
public sealed record OAuthStateEntry(string? Data);

/// <summary>
/// Short-lived, single-use <c>state</c> values for an authorization-code sign-in (RFC 6749 §10.12): created when the
/// browser is sent to the provider, consumed once on the callback.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bind the state to the browser.</b> A state that any browser may redeem does not stop login CSRF: an attacker
/// starts a sign-in with their own provider account, stops before the callback, and sends the victim the callback URL
/// with the attacker's code and a valid state, signing the victim in as the attacker. Pass a <c>binding</c> that only
/// the initiating browser holds (the value of a random, HttpOnly cookie set when the sign-in starts) to
/// <see cref="CreateAsync"/> and the same cookie's value to <see cref="TryConsumeAsync"/>: a state presented with any
/// other binding is refused. A null binding creates an unbound state, which only proves the state was issued.
/// </para>
/// <para>
/// Implement this for a multi-node host (a shared store), so a callback landing on another node still finds the state.
/// Consumption must be atomic: two concurrent callbacks with one state may succeed at most once.
/// </para>
/// </remarks>
public interface IOAuthStateStore
{
    /// <summary>Creates and stores a fresh, unguessable state and returns it.</summary>
    /// <param name="binding">A value only the initiating browser holds (see remarks), or null for an unbound state.</param>
    /// <param name="data">A value to hand back on consumption (e.g. a PKCE verifier), or null.</param>
    Task<string> CreateAsync(string? binding, string? data = null, CancellationToken ct = default);

    /// <summary>
    /// Consumes <paramref name="state"/>: returns its entry when it exists, has not expired and was created with the same
    /// <paramref name="binding"/>, and null otherwise. A state is removed by its first presentation, whatever the
    /// outcome, so a wrong binding cannot be retried against it.
    /// </summary>
    Task<OAuthStateEntry?> TryConsumeAsync(string? state, string? binding, CancellationToken ct = default);
}

/// <summary>Options for <see cref="InMemoryOAuthStateStore"/>.</summary>
public sealed class OAuthStateStoreOptions
{
    /// <summary>How long a state stays valid. Default 10 minutes.</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The most states held at once. Default 10 000. Starting a sign-in needs no authentication, so without a bound
    /// anyone could fill the store; once full, expired states are swept and then the oldest is evicted.
    /// </summary>
    public int MaxPending { get; set; } = 10_000;
}

/// <summary>
/// The in-process <see cref="IOAuthStateStore"/>: a single node, states lost on restart (a sign-in takes seconds).
/// Register it as a singleton. Consumption is atomic (<c>TryRemove</c>), and expiry follows the given
/// <see cref="TimeProvider"/>.
/// </summary>
public sealed class InMemoryOAuthStateStore : IOAuthStateStore
{
    private sealed record Pending(DateTimeOffset ExpiresAt, string? BindingHash, string? Data);

    private readonly ConcurrentDictionary<string, Pending> _states = new(StringComparer.Ordinal);
    private readonly OAuthStateStoreOptions _options;
    private readonly TimeProvider _clock;

    public InMemoryOAuthStateStore(OAuthStateStoreOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new OAuthStateStoreOptions();
        if (_options.Lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), _options.Lifetime, "Lifetime must be positive.");
        if (_options.MaxPending <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxPending, "MaxPending must be positive.");
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The number of states held, expired ones included until they are swept.</summary>
    public int Count => _states.Count;

    /// <inheritdoc />
    public Task<string> CreateAsync(string? binding, string? data = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_states.Count >= _options.MaxPending)
            MakeRoom();

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        _states[state] = new Pending(_clock.GetUtcNow() + _options.Lifetime, Hash(binding), data);
        return Task.FromResult(state);
    }

    /// <inheritdoc />
    public Task<OAuthStateEntry?> TryConsumeAsync(string? state, string? binding, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(state) || !_states.TryRemove(state, out var pending))
            return Task.FromResult<OAuthStateEntry?>(null);
        if (pending.ExpiresAt <= _clock.GetUtcNow())
            return Task.FromResult<OAuthStateEntry?>(null);
        if (!BindingMatches(pending.BindingHash, Hash(binding)))
            return Task.FromResult<OAuthStateEntry?>(null);
        return Task.FromResult<OAuthStateEntry?>(new OAuthStateEntry(pending.Data));
    }

    /// <summary>Removes every expired state. Called when the store is full; a host may also call it on a timer.</summary>
    public void Sweep()
    {
        var now = _clock.GetUtcNow();
        foreach (var (key, value) in _states)
        {
            if (value.ExpiresAt <= now)
                _states.TryRemove(new System.Collections.Generic.KeyValuePair<string, Pending>(key, value));
        }
    }

    private void MakeRoom()
    {
        Sweep();
        while (_states.Count >= _options.MaxPending)
        {
            var oldest = _states.OrderBy(kv => kv.Value.ExpiresAt).FirstOrDefault();
            if (oldest.Key == null || !_states.TryRemove(oldest)) break;
        }
    }

    // Stored hashed, and compared in constant time, so a binding cookie's value is not held in memory or timed.
    private static string? Hash(string? binding) =>
        binding == null ? null : Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(binding)));

    private static bool BindingMatches(string? stored, string? presented)
    {
        if (stored == null || presented == null) return stored == presented;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(stored), System.Text.Encoding.ASCII.GetBytes(presented));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
