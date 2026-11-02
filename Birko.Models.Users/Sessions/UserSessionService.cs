using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;

namespace Birko.Models.Users.Sessions
{
    /// <summary>
    /// Signed-in devices as rows (TASK-805): opens a session per sign-in, rotates its refresh token on every refresh,
    /// keeps its tenant, and revokes one session or all of a user's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Rotation is a claim.</b> A refresh writes the new token's hash with a filtered update that matches only while the
    /// row still holds the presented hash, then re-reads the row to see whose write stuck. On SQL that update is one
    /// conditional statement, so of two refreshes with one token exactly one gets a new token; the other gets
    /// <see cref="SessionRefreshStatus.Raced"/> and nothing is revoked (two tabs at once are not an attack). On a store
    /// that runs a filtered update as read-then-write, the claim is only as atomic as that store.
    /// </para>
    /// <para>
    /// <b>Reuse.</b> The row keeps the hash it was last rotated from. That token presented again means a copy of it is in
    /// someone else's hands, so the session is revoked (<see cref="SessionRefreshStatus.Reused"/>). Only the latest
    /// previous token is remembered; an older one is simply <see cref="SessionRefreshStatus.Invalid"/>.
    /// </para>
    /// <para>
    /// <b>Tokens issued before sessions.</b> With a <c>UserLogin</c> store, a token found on
    /// <see cref="UserLogin.RefreshToken"/> is redeemed once: the column is cleared and the sign-in continues on a new
    /// session, with the tenant <see cref="UserSessionOptions.LegacyTenant"/> reads from the token. Nobody is signed out
    /// by the upgrade.
    /// </para>
    /// <para>
    /// The service does not open a transaction; run it inside the app's unit of work where one is wanted.
    /// </para>
    /// </remarks>
    public class UserSessionService
    {
        private const string MovedMarkerPrefix = "moved-to-session:";

        private readonly IAsyncBulkStore<UserSession> _sessions;
        private readonly IAsyncBulkStore<UserTenant>? _memberships;
        private readonly IAsyncBulkStore<UserLogin>? _logins;
        private readonly UserSessionOptions _options;
        private readonly TimeProvider _clock;

        /// <param name="sessions">The <c>UserSessions</c> store.</param>
        /// <param name="memberships">
        /// The <c>UserTenants</c> store: a refresh keeps the session's tenant only while the user is a member. Null skips
        /// the membership check (an app without tenants).
        /// </param>
        /// <param name="logins">
        /// The <c>UserLogins</c> store, to redeem tokens issued before sessions and to clear them at a revoke-all. Null once
        /// no such token can be left.
        /// </param>
        public UserSessionService(
            IAsyncBulkStore<UserSession> sessions,
            IAsyncBulkStore<UserTenant>? memberships = null,
            IAsyncBulkStore<UserLogin>? logins = null,
            UserSessionOptions? options = null,
            TimeProvider? clock = null)
        {
            _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _memberships = memberships;
            _logins = logins;
            _options = options ?? new UserSessionOptions();
            _clock = clock ?? TimeProvider.System;
            if (_options.Lifetime <= TimeSpan.Zero)
                throw new ArgumentException("UserSessionOptions.Lifetime must be positive.", nameof(options));
        }

        private DateTime Now => _clock.GetUtcNow().UtcDateTime;

        /// <summary>SHA-256 of a refresh token, hex uppercase: what a session row stores.</summary>
        public static string HashToken(string token)
        {
            if (token == null) throw new ArgumentNullException(nameof(token));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        /// <summary>
        /// Opens a session for a sign-in and returns its refresh token. Also deletes the user's own expired rows, and
        /// revoked ones past <see cref="UserSessionOptions.RevokedRetention"/>.
        /// </summary>
        public async Task<IssuedSession> CreateAsync(
            Guid userGuid, Guid? tenantGuid = null, Guid? userLoginGuid = null, string? deviceLabel = null, CancellationToken ct = default)
        {
            var now = Now;
            await PruneAsync(userGuid, now, ct).ConfigureAwait(false);

            var token = NewToken();
            var session = new UserSession
            {
                UserGuid = userGuid,
                UserLoginGuid = userLoginGuid,
                TokenHash = HashToken(token),
                TenantGuid = tenantGuid,
                DeviceLabel = Label(deviceLabel),
                LastUsedAt = now,
                ExpiresAt = now + _options.Lifetime,
            };
            await _sessions.CreateAsync(session, ct: ct).ConfigureAwait(false);
            return new IssuedSession(session, token);
        }

        /// <summary>
        /// Redeems a refresh token: a new token on the same session, its tenant kept while the user is still a member and
        /// <see cref="UserSessionOptions.TenantAllowed"/> agrees.
        /// </summary>
        public async Task<SessionRefreshResult> RefreshAsync(string token, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token))
                return SessionRefreshResult.Failed(SessionRefreshStatus.Invalid);

            var now = Now;
            var hash = HashToken(token);
            var session = await _sessions.ReadFirstAsync(s => s.TokenHash == hash, ct).ConfigureAwait(false);
            if (session == null)
            {
                var rotatedFrom = await _sessions.ReadFirstAsync(s => s.PreviousTokenHash == hash, ct).ConfigureAwait(false);
                if (rotatedFrom != null)
                {
                    if (rotatedFrom.RevokedAt != null)
                        return SessionRefreshResult.Failed(SessionRefreshStatus.Revoked, rotatedFrom);
                    var revoked = await RevokeRowAsync(rotatedFrom.Guid!.Value, SessionRevokeReasons.Reuse, now, ct).ConfigureAwait(false);
                    return SessionRefreshResult.Failed(SessionRefreshStatus.Reused, revoked ?? rotatedFrom);
                }

                session = await MoveLegacyTokenAsync(token, now, ct).ConfigureAwait(false);
                if (session == null)
                    return SessionRefreshResult.Failed(SessionRefreshStatus.Invalid);
                hash = session.TokenHash;
            }

            return await RotateAsync(session, hash, now, keepTenant: true, newTenant: null, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Moves one session into <paramref name="tenantGuid"/> and rotates its token; the user's other sessions keep their
        /// tenants. The app has already decided the user may enter that tenant.
        /// </summary>
        /// <param name="sessionGuid">The session the request came from (put it in the access token, e.g. as <c>sid</c>).</param>
        /// <param name="userGuid">The caller: a session of another user is <see cref="SessionRefreshStatus.Invalid"/>.</param>
        public async Task<SessionRefreshResult> SwitchTenantAsync(Guid sessionGuid, Guid userGuid, Guid tenantGuid, CancellationToken ct = default)
        {
            var session = await _sessions.ReadAsync(sessionGuid, ct).ConfigureAwait(false);
            if (session == null || session.UserGuid != userGuid)
                return SessionRefreshResult.Failed(SessionRefreshStatus.Invalid);
            return await RotateAsync(session, session.TokenHash, Now, keepTenant: false, newTenant: tenantGuid, ct).ConfigureAwait(false);
        }

        /// <summary>Signs one session out. False when it is not this user's, or was already revoked.</summary>
        public async Task<bool> RevokeAsync(Guid sessionGuid, Guid userGuid, string reason = SessionRevokeReasons.Logout, CancellationToken ct = default)
        {
            var session = await _sessions.ReadAsync(sessionGuid, ct).ConfigureAwait(false);
            if (session == null || session.UserGuid != userGuid || session.RevokedAt != null)
                return false;
            var after = await RevokeRowAsync(sessionGuid, reason, Now, ct).ConfigureAwait(false);
            return after?.RevokedReason == reason && after.RevokedAt != null;
        }

        /// <summary>Signs out the session holding <paramref name="token"/> (a logout that presents its refresh token).</summary>
        public async Task<bool> RevokeByTokenAsync(string token, string reason = SessionRevokeReasons.Logout, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(token)) return false;
            var hash = HashToken(token);
            var session = await _sessions.ReadFirstAsync(s => s.TokenHash == hash, ct).ConfigureAwait(false);
            if (session == null) return false;
            return await RevokeAsync(session.Guid!.Value, session.UserGuid, reason, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Signs the user out everywhere: logout-everywhere, a password change, an admin reset. Also clears any token still
        /// on the user's <c>UserLogin</c> rows from before sessions, so none stays redeemable.
        /// </summary>
        /// <param name="exceptSessionGuid">A session to keep, e.g. the one changing its password.</param>
        public async Task RevokeAllAsync(Guid userGuid, string reason = SessionRevokeReasons.LogoutEverywhere, Guid? exceptSessionGuid = null, CancellationToken ct = default)
        {
            var now = Now;
            Expression<Func<UserSession, bool>> scope = exceptSessionGuid is Guid keep
                ? s => s.UserGuid == userGuid && s.RevokedAt == null && s.Guid != keep
                : s => s.UserGuid == userGuid && s.RevokedAt == null;
            await _sessions.UpdateAsync(scope,
                new PropertyUpdate<UserSession>().Set(s => s.RevokedAt, (DateTime?)now).Set(s => s.RevokedReason, (string?)reason), ct)
                .ConfigureAwait(false);

            if (_logins == null) return;
            await _logins.UpdateAsync(l => l.UserGuid == userGuid && l.RefreshToken != null,
                new PropertyUpdate<UserLogin>().Set(l => l.RefreshToken, (string?)null).Set(l => l.RefreshTokenExpiry, (DateTime?)null), ct)
                .ConfigureAwait(false);
        }

        /// <summary>The user's sessions that can still refresh, most recently used first, for a "where am I signed in" page.</summary>
        public async Task<IReadOnlyList<ViewModels.UserSession>> ListActiveAsync(Guid userGuid, CancellationToken ct = default)
        {
            var now = Now;
            var rows = await _sessions.ReadAsync(s => s.UserGuid == userGuid && s.RevokedAt == null && s.ExpiresAt > now, null, null, null, ct)
                .ConfigureAwait(false);
            return rows.OrderByDescending(s => s.LastUsedAt).Select(s =>
            {
                var view = new ViewModels.UserSession();
                view.LoadFrom(s);
                return view;
            }).ToList();
        }

        /// <summary>
        /// Deletes every expired session, and revoked ones past <see cref="UserSessionOptions.RevokedRetention"/>. Run it
        /// from a recurring job; <see cref="CreateAsync"/> already does it for the signing-in user.
        /// </summary>
        public Task PruneAsync(CancellationToken ct = default)
        {
            var now = Now;
            var revokedBefore = now - _options.RevokedRetention;
            return _sessions.DeleteAsync(s => s.ExpiresAt <= now || (s.RevokedAt != null && s.RevokedAt <= revokedBefore), ct);
        }

        private Task PruneAsync(Guid userGuid, DateTime now, CancellationToken ct)
        {
            var revokedBefore = now - _options.RevokedRetention;
            return _sessions.DeleteAsync(s => s.UserGuid == userGuid && (s.ExpiresAt <= now || (s.RevokedAt != null && s.RevokedAt <= revokedBefore)), ct);
        }

        private async Task<SessionRefreshResult> RotateAsync(
            UserSession session, string presentedHash, DateTime now, bool keepTenant, Guid? newTenant, CancellationToken ct)
        {
            if (session.RevokedAt != null)
                return SessionRefreshResult.Failed(SessionRefreshStatus.Revoked, session);
            if (session.ExpiresAt <= now)
                return SessionRefreshResult.Failed(SessionRefreshStatus.Expired, session);

            var guid = session.Guid!.Value;
            if (_options.UserAllowed != null && !await _options.UserAllowed(session.UserGuid, ct).ConfigureAwait(false))
            {
                var revoked = await RevokeRowAsync(guid, SessionRevokeReasons.UserRefused, now, ct).ConfigureAwait(false);
                return SessionRefreshResult.Failed(SessionRefreshStatus.UserRefused, revoked ?? session);
            }

            var tenant = newTenant;
            var dropped = false;
            if (keepTenant && session.TenantGuid is Guid current)
            {
                if (await TenantStillAllowedAsync(session.UserGuid, current, ct).ConfigureAwait(false))
                {
                    tenant = current;
                }
                else
                {
                    dropped = true;
                }
            }

            var token = NewToken();
            var newHash = HashToken(token);
            await _sessions.UpdateAsync(s => s.Guid == guid && s.TokenHash == presentedHash && s.RevokedAt == null,
                new PropertyUpdate<UserSession>()
                    .Set(s => s.TokenHash, newHash)
                    .Set(s => s.PreviousTokenHash, (string?)presentedHash)
                    .Set(s => s.TenantGuid, tenant)
                    .Set(s => s.LastUsedAt, now)
                    .Set(s => s.ExpiresAt, now + _options.Lifetime), ct)
                .ConfigureAwait(false);

            // Whose write stuck: ours only if the row now holds our hash.
            var after = await _sessions.ReadAsync(guid, CancellationToken.None).ConfigureAwait(false);
            if (after == null)
                return SessionRefreshResult.Failed(SessionRefreshStatus.Invalid);
            if (after.TokenHash == newHash)
                return SessionRefreshResult.Refreshed(after, token, dropped);
            if (after.RevokedAt != null)
                return SessionRefreshResult.Failed(SessionRefreshStatus.Revoked, after);
            return SessionRefreshResult.Failed(SessionRefreshStatus.Raced, after);
        }

        private async Task<bool> TenantStillAllowedAsync(Guid userGuid, Guid tenantGuid, CancellationToken ct)
        {
            if (_memberships != null
                && await _memberships.CountAsync(m => m.UserGuid == userGuid && m.TenantGuid == tenantGuid, ct).ConfigureAwait(false) == 0)
                return false;
            return _options.TenantAllowed == null || await _options.TenantAllowed(userGuid, tenantGuid, ct).ConfigureAwait(false);
        }

        private async Task<UserSession?> RevokeRowAsync(Guid sessionGuid, string reason, DateTime now, CancellationToken ct)
        {
            await _sessions.UpdateAsync(s => s.Guid == sessionGuid && s.RevokedAt == null,
                new PropertyUpdate<UserSession>().Set(s => s.RevokedAt, (DateTime?)now).Set(s => s.RevokedReason, (string?)reason), ct)
                .ConfigureAwait(false);
            return await _sessions.ReadAsync(sessionGuid, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// A token from before sessions: claim it off its <c>UserLogin</c> row (a marker only this call knows, so of two
        /// concurrent redemptions one wins), open a session holding it, then clear the column.
        /// </summary>
        private async Task<UserSession?> MoveLegacyTokenAsync(string token, DateTime now, CancellationToken ct)
        {
            if (_logins == null || token.StartsWith(MovedMarkerPrefix, StringComparison.Ordinal))
                return null;
            var login = await _logins.ReadFirstAsync(l => l.RefreshToken == token, ct).ConfigureAwait(false);
            if (login == null || login.RefreshTokenExpiry is not DateTime expiry || expiry <= now)
                return null;

            var loginGuid = login.Guid!.Value;
            var marker = MovedMarkerPrefix + Guid.NewGuid().ToString("N");
            await _logins.UpdateAsync(l => l.Guid == loginGuid && l.RefreshToken == token,
                new PropertyUpdate<UserLogin>().Set(l => l.RefreshToken, (string?)marker), ct).ConfigureAwait(false);
            var claimed = await _logins.ReadAsync(loginGuid, CancellationToken.None).ConfigureAwait(false);
            if (claimed?.RefreshToken != marker)
                return null;

            var session = new UserSession
            {
                UserGuid = login.UserGuid,
                UserLoginGuid = loginGuid,
                TokenHash = HashToken(token),
                TenantGuid = _options.LegacyTenant?.Invoke(token),
                LastUsedAt = now,
                ExpiresAt = expiry,
            };
            await _sessions.CreateAsync(session, ct: ct).ConfigureAwait(false);

            await _logins.UpdateAsync(l => l.Guid == loginGuid && l.RefreshToken == marker,
                new PropertyUpdate<UserLogin>().Set(l => l.RefreshToken, (string?)null).Set(l => l.RefreshTokenExpiry, (DateTime?)null), ct)
                .ConfigureAwait(false);
            return session;
        }

        private string? Label(string? deviceLabel)
        {
            if (string.IsNullOrWhiteSpace(deviceLabel)) return null;
            var label = deviceLabel.Trim();
            return label.Length <= _options.MaxDeviceLabelLength ? label : label.Substring(0, _options.MaxDeviceLabelLength);
        }

        private static string NewToken()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
