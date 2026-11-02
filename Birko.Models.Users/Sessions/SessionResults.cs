namespace Birko.Models.Users.Sessions
{
    /// <summary>A session just opened, and its refresh token. The token is shown here once and stored nowhere.</summary>
    public sealed class IssuedSession
    {
        public IssuedSession(UserSession session, string token)
        {
            Session = session;
            Token = token;
        }

        public UserSession Session { get; }

        public string Token { get; }
    }

    public enum SessionRefreshStatus
    {
        /// <summary>A new token was issued on the same session.</summary>
        Refreshed,

        /// <summary>No session holds this token.</summary>
        Invalid,

        Expired,

        Revoked,

        /// <summary>A token this session had already rotated away from: the session is now revoked.</summary>
        Reused,

        /// <summary>
        /// Another refresh with the same token won (two tabs at once). Nothing is revoked; the winner holds the new token.
        /// </summary>
        Raced,

        /// <summary><see cref="UserSessionOptions.UserAllowed"/> said no: the session is now revoked.</summary>
        UserRefused,
    }

    /// <summary>The answer to <see cref="UserSessionService.RefreshAsync"/> and <see cref="UserSessionService.SwitchTenantAsync"/>.</summary>
    public sealed class SessionRefreshResult
    {
        private SessionRefreshResult(SessionRefreshStatus status, UserSession? session, string? token, bool tenantDropped)
        {
            Status = status;
            Session = session;
            Token = token;
            TenantDropped = tenantDropped;
        }

        public SessionRefreshStatus Status { get; }

        public bool Succeeded => Status == SessionRefreshStatus.Refreshed;

        /// <summary>The session as stored after the call; null when no session matched.</summary>
        public UserSession? Session { get; }

        /// <summary>The new refresh token, only when <see cref="Succeeded"/>.</summary>
        public string? Token { get; }

        /// <summary>
        /// The session had a tenant the user may no longer use, so it was cleared: pick one as at login and
        /// <see cref="UserSessionService.SwitchTenantAsync"/>, or issue the access token for the one you pick.
        /// </summary>
        public bool TenantDropped { get; }

        internal static SessionRefreshResult Refreshed(UserSession session, string token, bool tenantDropped)
            => new(SessionRefreshStatus.Refreshed, session, token, tenantDropped);

        internal static SessionRefreshResult Failed(SessionRefreshStatus status, UserSession? session = null)
            => new(status, session, null, false);
    }
}
