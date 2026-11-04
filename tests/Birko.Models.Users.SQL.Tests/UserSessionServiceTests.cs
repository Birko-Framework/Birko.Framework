using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using Birko.Models.Users.Sessions;
using Birko.Models.Users.SQL.Mappings;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Users.SQL.Tests;

/// <summary>
/// TASK-805. <see cref="UserSessionService"/> on a real SQLite database: a row per signed-in device, rotation as a claim,
/// reuse detection, the tenant on the row, and tokens from before sessions moved over once.
/// </summary>
public sealed class UserSessionServiceTests : IAsyncLifetime
{
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A session store that can run something just before a filtered update reaches the database.</summary>
    private sealed class InterceptingSessionStore : AsyncSQLiteStore<UserSession>
    {
        public Func<Task>? BeforeFilteredUpdate { get; set; }

        public override async Task UpdateAsync(Expression<Func<UserSession, bool>> filter, PropertyUpdate<UserSession> updates, CancellationToken ct = default)
        {
            var hook = BeforeFilteredUpdate;
            BeforeFilteredUpdate = null;
            if (hook != null) await hook();
            await base.UpdateAsync(filter, updates, ct);
        }
    }

    private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"birko-sessions-{Guid.NewGuid():N}");
    private readonly SqLiteSettings _settings;
    private readonly InterceptingSessionStore _sessions = new();
    private readonly AsyncSQLiteStore<UserTenant> _memberships = new();
    private readonly AsyncSQLiteStore<UserLogin> _logins = new();
    private readonly ManualClock _clock = new();
    private readonly UserSessionOptions _options = new();
    private readonly UserSessionService _service;

    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _shopA = Guid.NewGuid();
    private readonly Guid _shopB = Guid.NewGuid();

    public UserSessionServiceTests()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new UserSessionMapping());
        registry.Register(new UserTenantMapping());
        registry.Register(new UserLoginMapping());
        registry.ApplyToDatabase();

        System.IO.Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, "sessions.db");
        _sessions.SetSettings(_settings);
        _memberships.SetSettings(_settings);
        _logins.SetSettings(_settings);
        _service = new UserSessionService(_sessions, _memberships, _logins, _options, _clock);
    }

    public async Task InitializeAsync()
    {
        await _memberships.CreateAsync(new UserTenant { UserGuid = _user, TenantGuid = _shopA });
        await _memberships.CreateAsync(new UserTenant { UserGuid = _user, TenantGuid = _shopB });
    }

    public Task DisposeAsync()
    {
        using (var probe = new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString()))
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(probe);
        TestTempDirectory.Delete(_root);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TwoDevices_BothRefresh_EachOnItsOwnRow()
    {
        var pc = await _service.CreateAsync(_user, _shopA, deviceLabel: "PC");
        var phone = await _service.CreateAsync(_user, _shopA, deviceLabel: "Phone");

        var pcRefresh = await _service.RefreshAsync(pc.Token);
        var phoneRefresh = await _service.RefreshAsync(phone.Token);

        pcRefresh.Status.Should().Be(SessionRefreshStatus.Refreshed);
        phoneRefresh.Status.Should().Be(SessionRefreshStatus.Refreshed, "signing in on a phone must not sign the PC out");
        pcRefresh.Session!.Guid.Should().Be(pc.Session.Guid);
        phoneRefresh.Session!.Guid.Should().Be(phone.Session.Guid);
        (await _service.RefreshAsync(pcRefresh.Token!)).Status.Should().Be(SessionRefreshStatus.Refreshed);
    }

    [Fact]
    public async Task SwitchOnOneDevice_LeavesTheOthersTenant()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        var phone = await _service.CreateAsync(_user, _shopA);

        var switched = await _service.SwitchTenantAsync(phone.Session.Guid!.Value, _user, _shopB);
        switched.Status.Should().Be(SessionRefreshStatus.Refreshed);

        (await _service.RefreshAsync(switched.Token!)).Session!.TenantGuid.Should().Be(_shopB, "a refresh keeps the tenant switched into");
        var pcRefresh = await _service.RefreshAsync(pc.Token);
        pcRefresh.Session!.TenantGuid.Should().Be(_shopA, "the switch moved only the phone's session");
        pcRefresh.TenantDropped.Should().BeFalse();
    }

    [Fact]
    public async Task SwitchTenant_OfAnotherUsersSession_IsInvalid()
    {
        var pc = await _service.CreateAsync(_user, _shopA);

        var result = await _service.SwitchTenantAsync(pc.Session.Guid!.Value, Guid.NewGuid(), _shopB);

        result.Status.Should().Be(SessionRefreshStatus.Invalid);
        (await _sessions.ReadAsync(pc.Session.Guid!.Value))!.TenantGuid.Should().Be(_shopA);
    }

    [Fact]
    public async Task ARotatedToken_PresentedAgain_RevokesTheSession()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        var rotated = await _service.RefreshAsync(pc.Token);

        var replay = await _service.RefreshAsync(pc.Token);

        replay.Status.Should().Be(SessionRefreshStatus.Reused);
        replay.Session!.RevokedReason.Should().Be(SessionRevokeReasons.Reuse);
        (await _service.RefreshAsync(rotated.Token!)).Status.Should().Be(SessionRefreshStatus.Revoked,
            "after a reuse the current holder is cut off too: either of them may be the thief");
    }

    [Fact]
    public async Task ALostClaim_IsRaced_AndRevokesNothing()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        SessionRefreshResult? winner = null;
        // The other tab's refresh with the same token lands between this refresh's read and its write.
        _sessions.BeforeFilteredUpdate = async () => winner = await _service.RefreshAsync(pc.Token);

        var loser = await _service.RefreshAsync(pc.Token);

        winner!.Status.Should().Be(SessionRefreshStatus.Refreshed);
        loser.Status.Should().Be(SessionRefreshStatus.Raced);
        loser.Token.Should().BeNull();
        (await _service.RefreshAsync(winner.Token!)).Status.Should().Be(SessionRefreshStatus.Refreshed, "two tabs at once are not an attack");
    }

    [Fact]
    public async Task RevokeAll_SignsOutEveryDevice_AndKillsATokenFromBeforeSessions()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        var phone = await _service.CreateAsync(_user, _shopB);
        var other = await _service.CreateAsync(Guid.NewGuid());
        var legacy = await LegacyLoginAsync("old-plain-token");

        await _service.RevokeAllAsync(_user, SessionRevokeReasons.PasswordChanged);

        (await _service.RefreshAsync(pc.Token)).Status.Should().Be(SessionRefreshStatus.Revoked);
        (await _service.RefreshAsync(phone.Token)).Status.Should().Be(SessionRefreshStatus.Revoked);
        (await _service.RefreshAsync("old-plain-token")).Status.Should().Be(SessionRefreshStatus.Invalid);
        (await _logins.ReadAsync(legacy.Guid!.Value))!.RefreshToken.Should().BeNull();
        (await _service.RefreshAsync(other.Token)).Status.Should().Be(SessionRefreshStatus.Refreshed, "another user's session is untouched");
        (await _sessions.ReadAsync(pc.Session.Guid!.Value))!.RevokedReason.Should().Be(SessionRevokeReasons.PasswordChanged);
    }

    [Fact]
    public async Task RevokeAll_KeepsTheExceptedSession()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        var phone = await _service.CreateAsync(_user, _shopA);

        await _service.RevokeAllAsync(_user, SessionRevokeReasons.PasswordChanged, exceptSessionGuid: pc.Session.Guid);

        (await _service.RefreshAsync(pc.Token)).Status.Should().Be(SessionRefreshStatus.Refreshed);
        (await _service.RefreshAsync(phone.Token)).Status.Should().Be(SessionRefreshStatus.Revoked);
    }

    [Fact]
    public async Task Logout_RevokesOnlyThisDevice()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        var phone = await _service.CreateAsync(_user, _shopA);

        (await _service.RevokeAsync(phone.Session.Guid!.Value, Guid.NewGuid())).Should().BeFalse("not the caller's session");
        (await _service.RevokeAsync(phone.Session.Guid!.Value, _user)).Should().BeTrue();
        (await _service.RevokeByTokenAsync(phone.Token)).Should().BeFalse("already revoked");

        (await _service.RefreshAsync(phone.Token)).Status.Should().Be(SessionRefreshStatus.Revoked);
        (await _service.RefreshAsync(pc.Token)).Status.Should().Be(SessionRefreshStatus.Refreshed);
    }

    [Fact]
    public async Task TheStoredHash_IsNotTheToken_AndTheViewModelHasNoHash()
    {
        var pc = await _service.CreateAsync(_user, _shopA, deviceLabel: "PC");

        var row = (await _sessions.ReadAsync(pc.Session.Guid!.Value))!;
        row.TokenHash.Should().NotBe(pc.Token).And.Be(UserSessionService.HashToken(pc.Token)).And.HaveLength(64);
        typeof(ViewModels.UserSession).GetProperties().Select(p => p.Name).Should().NotContain(n => n.Contains("Token") || n.Contains("Hash"));

        var listed = await _service.ListActiveAsync(_user);
        listed.Should().ContainSingle().Which.DeviceLabel.Should().Be("PC");
    }

    [Fact]
    public async Task APlainTokenFromBeforeSessions_IsRedeemedOnce_OntoASession()
    {
        var login = await LegacyLoginAsync("old-plain-token");

        var first = await _service.RefreshAsync("old-plain-token");

        first.Status.Should().Be(SessionRefreshStatus.Refreshed, "the upgrade signs nobody out");
        first.Session!.UserGuid.Should().Be(_user);
        first.Session.UserLoginGuid.Should().Be(login.Guid);
        (await _logins.ReadAsync(login.Guid!.Value))!.RefreshToken.Should().BeNull("the column is cleared once moved");
        (await _service.RefreshAsync("old-plain-token")).Status.Should().Be(SessionRefreshStatus.Reused,
            "the old token was rotated away from on its new session, like any other");
    }

    [Fact]
    public async Task ATokenCarryingItsTenant_LandsOnTheSessionInThatTenant()
    {
        _options.LegacyTenant = token => token.Split('.') is [_, var suffix] && Guid.TryParseExact(suffix, "N", out var t) ? t : null;
        var token = $"random.{_shopB:N}";
        await LegacyLoginAsync(token);

        var result = await _service.RefreshAsync(token);

        result.Status.Should().Be(SessionRefreshStatus.Refreshed);
        result.Session!.TenantGuid.Should().Be(_shopB);
    }

    [Fact]
    public async Task AnExpiredTokenFromBeforeSessions_IsNotMoved()
    {
        await LegacyLoginAsync("old-plain-token", expiresIn: TimeSpan.FromMinutes(-1));

        (await _service.RefreshAsync("old-plain-token")).Status.Should().Be(SessionRefreshStatus.Invalid);
        (await _sessions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheTenant_IsDropped_WhenTheUserLeftIt()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        await _memberships.DeleteAsync(m => m.UserGuid == _user && m.TenantGuid == _shopA);

        var result = await _service.RefreshAsync(pc.Token);

        result.Status.Should().Be(SessionRefreshStatus.Refreshed);
        result.TenantDropped.Should().BeTrue();
        result.Session!.TenantGuid.Should().BeNull();
    }

    [Fact]
    public async Task TheTenant_IsDropped_WhenTheAppSaysNo()
    {
        _options.TenantAllowed = (_, tenant, _) => Task.FromResult(tenant != _shopA);
        var pc = await _service.CreateAsync(_user, _shopA);
        var phone = await _service.CreateAsync(_user, _shopB);

        (await _service.RefreshAsync(pc.Token)).TenantDropped.Should().BeTrue();
        (await _service.RefreshAsync(phone.Token)).Session!.TenantGuid.Should().Be(_shopB);
    }

    [Fact]
    public async Task AUserTheAppRefuses_IsSignedOut()
    {
        _options.UserAllowed = (_, _) => Task.FromResult(false);
        var pc = await _service.CreateAsync(_user, _shopA);

        var result = await _service.RefreshAsync(pc.Token);

        result.Status.Should().Be(SessionRefreshStatus.UserRefused);
        result.Session!.RevokedReason.Should().Be(SessionRevokeReasons.UserRefused);
    }

    [Fact]
    public async Task AnExpiredSession_IsRefused_AndPruned()
    {
        var pc = await _service.CreateAsync(_user, _shopA);
        _clock.Now += _options.Lifetime - TimeSpan.FromDays(1);
        // Revoked, but its lifetime has days left: only the retention rule can delete it.
        var phone = await _service.CreateAsync(_user, _shopA);
        await _service.RevokeAsync(phone.Session.Guid!.Value, _user);
        var kept = await _service.CreateAsync(_user, _shopA);

        _clock.Now += _options.RevokedRetention + TimeSpan.FromHours(1);
        (await _service.RefreshAsync(pc.Token)).Status.Should().Be(SessionRefreshStatus.Expired);
        (await _sessions.ReadAsync(phone.Session.Guid!.Value))!.ExpiresAt.Should().BeAfter(_clock.Now.UtcDateTime);

        await _service.PruneAsync();

        (await _sessions.ReadAsync(pc.Session.Guid!.Value)).Should().BeNull("expired");
        (await _sessions.ReadAsync(phone.Session.Guid!.Value)).Should().BeNull("revoked past the retention");
        (await _sessions.ReadAsync(kept.Session.Guid!.Value)).Should().NotBeNull("still live");
    }

    [Fact]
    public async Task SigningIn_PrunesThatUsersDeadRows()
    {
        var old = await _service.CreateAsync(_user, _shopA);
        _clock.Now += _options.Lifetime + TimeSpan.FromMinutes(1);

        await _service.CreateAsync(_user, _shopA);

        (await _sessions.ReadAsync(old.Session.Guid!.Value)).Should().BeNull();
    }

    [Fact]
    public async Task OnSQLite_TheIndexesExist()
    {
        await _service.CreateAsync(_user, _shopA);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_settings.GetConnectionString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'UserSessions'";
        using var reader = command.ExecuteReader();
        var names = new System.Collections.Generic.List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        names.Should().Contain(new[] { "UX_UserSessions_TokenHash", "IX_UserSessions_PreviousTokenHash", "IX_UserSessions_UserGuid" });
    }

    private async Task<UserLogin> LegacyLoginAsync(string token, TimeSpan? expiresIn = null)
    {
        var login = new UserLogin
        {
            UserGuid = _user,
            Provider = "local",
            ProviderKey = $"user-{Guid.NewGuid():N}",
            RefreshToken = token,
            RefreshTokenExpiry = _clock.Now.UtcDateTime + (expiresIn ?? TimeSpan.FromDays(3)),
        };
        await _logins.CreateAsync(login);
        return login;
    }
}
