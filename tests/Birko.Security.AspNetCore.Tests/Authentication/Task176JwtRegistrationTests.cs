using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Birko.Time;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Authentication;

/// <summary>
/// TASK-176 — the JWT registration's low findings: SH-L221 (no algorithm allow-list on either symmetric validation path),
/// SH-L222 (a 4-character secret passed startup), SH-L227 (a null configure delegate was a NullReferenceException) and
/// SH-L228 (a registered IDateTimeProvider was ignored by the token provider).
/// </summary>
public class Task176JwtRegistrationTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"; // 64 chars: HS512 needs it

    private sealed class FixedClock(DateTime utc) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utc;
        public DateTime Now => UtcNow;
        public DateTimeOffset OffsetUtcNow => new(UtcNow);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private static ServiceProvider Build(Action<IServiceCollection>? extra = null, string secret = Secret)
    {
        var services = new ServiceCollection();
        extra?.Invoke(services);
        services.AddBirkoJwtBearer(o => { o.Secret = secret; o.Issuer = "birko"; o.Audience = "tests"; o.ExpirationMinutes = 30; });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_null_configure_is_ArgumentNullException_on_both_entry_points()
    {
        var services = new ServiceCollection();
        FluentActions.Invoking(() => services.AddBirkoJwtBearer(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => services.AddBirkoSecurity(null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(4)]
    [InlineData(31)]
    public void A_secret_shorter_than_32_characters_is_refused_at_registration(int length)
    {
        var act = () => Build(secret: new string('k', length));
        act.Should().Throw<ArgumentException>().WithMessage("*at least 32*");
    }

    [Fact]
    public void A_32_character_secret_is_accepted() =>
        FluentActions.Invoking(() => Build(secret: new string('k', 32))).Should().NotThrow();

    [Fact]
    public void A_registered_clock_stamps_the_tokens_the_provider_issues()
    {
        var at = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        using var sp = Build(s => s.AddSingleton<IDateTimeProvider>(new FixedClock(at)));

        var token = sp.GetRequiredService<ITokenProvider>().GenerateToken(new Dictionary<string, string> { ["sub"] = "u" });

        token.ExpiresAt.Should().Be(at.AddMinutes(30), "the registered clock, not the system clock, decides iat/exp");
    }

    [Fact]
    public void The_provider_validates_HS256_and_refuses_HS512_made_with_the_same_secret()
    {
        using var sp = Build();
        var provider = sp.GetRequiredService<ITokenProvider>();
        var hs256 = provider.GenerateToken(new Dictionary<string, string> { ["sub"] = "u" }).Token;

        var hs512 = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "birko", audience: "tests", claims: new[] { new Claim("sub", "u") },
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha512)));

        provider.ValidateToken(hs256).IsValid.Should().BeTrue("the control: the algorithm it mints");
        provider.ValidateToken(hs512).IsValid.Should().BeFalse("only HS256 is allowed (SH-L221)");
    }

    [Fact]
    public void The_bearer_handler_allows_only_HS256()
    {
        using var sp = Build();
        var bearer = sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

        bearer.TokenValidationParameters.ValidAlgorithms.Should().BeEquivalentTo(new[] { SecurityAlgorithms.HmacSha256 });
    }
}
