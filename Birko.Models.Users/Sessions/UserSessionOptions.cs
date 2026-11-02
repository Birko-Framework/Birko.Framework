using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Models.Users.Sessions
{
    /// <summary>Settings for <see cref="UserSessionService"/> (TASK-805).</summary>
    public sealed class UserSessionOptions
    {
        /// <summary>How long a refresh token lives; every refresh starts it again. Default 7 days.</summary>
        public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(7);

        /// <summary>
        /// How long a revoked row is kept before <see cref="UserSessionService.PruneAsync"/> deletes it (an expired row
        /// goes at once). Default 1 day.
        /// </summary>
        public TimeSpan RevokedRetention { get; set; } = TimeSpan.FromDays(1);

        /// <summary>
        /// The app's own say on a refresh, before a token is issued: false revokes the session
        /// (<see cref="SessionRevokeReasons.UserRefused"/>). Symbio: the user still exists and is active.
        /// </summary>
        public Func<Guid, CancellationToken, Task<bool>>? UserAllowed { get; set; }

        /// <summary>
        /// The app's own say on keeping the session's tenant (user, tenant), asked after the membership check passes.
        /// False drops the tenant from the session; the app then picks one as at login. Symbio: the tenant is active.
        /// </summary>
        public Func<Guid, Guid, CancellationToken, Task<bool>>? TenantAllowed { get; set; }

        /// <summary>
        /// Reads a tenant out of a token issued before sessions, when the app carried one in it (Symbio TASK-1035:
        /// <c>{random}.{tenantGuid:N}</c>). Null, or a null answer, moves the old token onto a session with no tenant.
        /// </summary>
        public Func<string, Guid?>? LegacyTenant { get; set; }

        /// <summary>A longer device label is cut to this length (the mapped column's). Default 256.</summary>
        public int MaxDeviceLabelLength { get; set; } = 256;
    }

    /// <summary>The reasons <see cref="UserSessionService"/> writes to <see cref="UserSession.RevokedReason"/>.</summary>
    public static class SessionRevokeReasons
    {
        public const string Logout = "logout";
        public const string LogoutEverywhere = "logout-everywhere";
        public const string PasswordChanged = "password-changed";
        public const string PasswordReset = "password-reset";

        /// <summary>A token this session had already rotated away from was presented again.</summary>
        public const string Reuse = "reuse";

        /// <summary><see cref="UserSessionOptions.UserAllowed"/> said no.</summary>
        public const string UserRefused = "user-refused";
    }
}
