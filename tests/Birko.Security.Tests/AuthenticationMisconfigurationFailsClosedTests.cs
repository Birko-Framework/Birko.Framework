using System;
using System.Collections.Generic;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// SH-H040 — <c>AuthenticationService.ValidateToken</c> allowed every caller when authentication was
/// switched <b>on</b> but misconfigured to nothing.
/// </summary>
/// <remarks>
/// <para>
/// <b>The mechanism.</b> <c>IsAuthenticationEnabled()</c> answers <c>Enabled &amp;&amp; (tokens or
/// bindings survived expansion)</c>, and the gate at the top of <c>ValidateToken</c> read it as
/// "enabled":
/// </para>
/// <code>
/// if (!IsAuthenticationEnabled()) return true;   // ← the defect
/// </code>
/// <para>
/// So two states that must be treated oppositely both meant allow-all: authentication deliberately
/// switched off (correct), and authentication switched on with an empty token set (an open endpoint).
/// The gate now tests <c>IsAuthenticationDisabled</c> — <c>!_config.Enabled</c> — which is the only
/// state an operator actually asked for.
/// </para>
/// <para>
/// ⚠ <b>The fix made existing code reachable rather than adding a branch.</b> <c>ValidateToken</c>
/// already ended with <c>LogWarning("Authentication enabled but no tokens or bindings configured");
/// return false;</c> — dead, because the top gate returned <c>true</c> for exactly that state. The
/// author's intent was fail-closed all along.
/// </para>
/// <para>
/// ⚠ <b>The finding's stated trigger was the safe case.</b> It blamed "a renamed <c>${VAR}</c> that
/// leaves Tokens empty". Measured: an absent variable makes
/// <c>Environment.GetEnvironmentVariable</c> return <c>null</c>, so
/// <c>ExpandEnvironmentVariable</c>'s <c>?? value</c> keeps the literal <c>"${VAR}"</c> — non-blank, so
/// it is retained, authentication stays on, and every real token is rejected. That fails <b>closed</b>.
/// The dangerous states are covered below: nothing configured at all, and a variable that exists but is
/// blank (where <c>GetEnvironmentVariable</c> returns <c>""</c>, the <c>??</c> never fires, and the
/// token is dropped). Anyone reproducing the finding as filed would have seen a 401 and called it a
/// false positive.
/// </para>
/// <para>
/// These live in <c>Birko.Security.Tests</c> — the engine's own project — although the pre-existing
/// coverage of this class sits in <c>Birko.Communication.WebSocket.Tests</c>. The defect is the
/// engine's, and four transports share it.
/// </para>
/// </remarks>
public class AuthenticationMisconfigurationFailsClosedTests
{
    private sealed class TestConfig : AuthenticationConfiguration
    {
    }

    private static AuthenticationService Service(bool enabled, params string[] tokens)
    {
        var config = new TestConfig { Enabled = enabled };
        config.Tokens.AddRange(tokens);
        return new AuthenticationService(config);
    }

    private static AuthenticationService ServiceWithBinding(bool enabled, string token, params string[] ips)
    {
        var config = new TestConfig { Enabled = enabled };
        config.TokenBindings.Add(new TokenBinding { Token = token, AllowedIps = new List<string>(ips) });
        return new AuthenticationService(config);
    }

    // ---- the defect: enabled, nothing configured --------------------------------------------------

    [Fact]
    public void Enabled_WithNothingConfigured_RejectsEveryCaller()
    {
        using var service = Service(enabled: true);

        // Before the fix all three returned true — an open endpoint on a config typo.
        service.ValidateToken("anything", "127.0.0.1").Should().BeFalse(
            "authentication is switched ON, so a misconfiguration must refuse rather than allow all");
        service.ValidateToken("", "127.0.0.1").Should().BeFalse();
        service.ValidateToken(null, "127.0.0.1").Should().BeFalse(
            "a caller presenting no token at all is the case that made this a bypass rather than a bug");
    }

    [Fact]
    public void Enabled_WithOnlyWhitespaceTokens_RejectsEveryCaller()
    {
        // InitializeCache drops whitespace, so the expanded set is empty even though Tokens is not.
        using var service = Service(enabled: true, "   ", "");

        service.ValidateToken("   ", "127.0.0.1").Should().BeFalse();
        service.ValidateToken(null, "127.0.0.1").Should().BeFalse();
        service.IsMisconfigured.Should().BeTrue(
            "the configuration lists tokens, but none of them survived expansion");
    }

    [Fact]
    public void Enabled_WithAnEnvironmentVariableSetToBlank_RejectsEveryCaller()
    {
        // The real dangerous env-var shape, and the one the finding did NOT name: the variable EXISTS
        // and is blank, so GetEnvironmentVariable returns "" rather than null, the `?? value` fallback
        // never fires, and IsNullOrWhiteSpace drops the token.
        const string name = "BIRKO_SH_H040_BLANK";
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, "");
        try
        {
            Environment.GetEnvironmentVariable(name).Should().Be("",
                "the premise of this test is that a blank variable reads back as empty, not null — "
                + "if this ever fails, the mechanism below has changed rather than the fix");

            using var service = Service(enabled: true, "${" + name + "}");

            service.IsMisconfigured.Should().BeTrue();
            service.ValidateToken("anything", "127.0.0.1").Should().BeFalse();
            service.ValidateToken(null, "127.0.0.1").Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    // ---- the contrast the finding got backwards ---------------------------------------------------

    [Fact]
    public void Enabled_WithAnAbsentEnvironmentVariable_AlreadyFailedClosed()
    {
        // The finding blamed this shape. It was never the bypass: the literal "${VAR}" is kept, so
        // authentication stays ON and every real token is refused. Pinned as the CONTRAST, so nobody
        // "fixes" the fallback and turns a fail-closed case into a fail-open one.
        const string name = "BIRKO_SH_H040_ABSENT_XYZ";
        Environment.GetEnvironmentVariable(name).Should().BeNull("this test needs the variable unset");

        using var service = Service(enabled: true, "${" + name + "}");

        service.IsMisconfigured.Should().BeFalse(
            "the literal placeholder was retained, so something IS configured — badly, but not emptily");
        service.IsAuthenticationEnabled().Should().BeTrue();
        service.ValidateToken("the-real-token", "127.0.0.1").Should().BeFalse("fails closed");
        service.ValidateToken("${" + name + "}", "127.0.0.1").Should().BeTrue(
            "the literal became the token — useless, but it is what the code does, and saying so is how "
            + "the next reader knows this branch was measured rather than assumed");
    }

    // ---- the opt-out, which must keep working -----------------------------------------------------

    [Fact]
    public void Disabled_StillAllowsEveryCaller()
    {
        // The opt-out that makes refusing a misconfiguration legitimate rather than a wall. Breaking
        // this would be a worse defect than the one being fixed, so it is asserted here and not left to
        // the pre-existing WebSocket test.
        using var service = Service(enabled: false, "secret");

        service.ValidateToken(null, "127.0.0.1").Should().BeTrue();
        service.ValidateToken("anything", "127.0.0.1").Should().BeTrue();
        service.IsAuthenticationDisabled.Should().BeTrue();
        service.IsMisconfigured.Should().BeFalse(
            "a deliberate switch-off is not a misconfiguration, and reporting it as one would train an "
            + "operator to ignore the signal");
    }

    [Fact]
    public void Disabled_WithNothingConfigured_IsNotReportedAsMisconfigured()
    {
        using var service = Service(enabled: false);

        service.IsMisconfigured.Should().BeFalse();
        service.ValidateToken(null, null).Should().BeTrue();
    }

    // ---- the configured happy path is untouched ---------------------------------------------------

    [Fact]
    public void Enabled_WithATokenConfigured_BehavesExactlyAsBefore()
    {
        using var service = Service(enabled: true, "secret");

        service.ValidateToken("secret", "127.0.0.1").Should().BeTrue();
        service.ValidateToken("wrong", "127.0.0.1").Should().BeFalse();
        service.ValidateToken(null, "127.0.0.1").Should().BeFalse();
        service.IsMisconfigured.Should().BeFalse();
        service.IsAuthenticationDisabled.Should().BeFalse();
    }

    [Fact]
    public void Enabled_WithOnlyABindingConfigured_IsNotMisconfigured()
    {
        // A binding alone is a valid configuration: IsMisconfigured must require BOTH collections empty,
        // or an IP-bound deployment would be reported broken and refused.
        using var service = ServiceWithBinding(enabled: true, "bound", "10.0.0.1");

        service.IsMisconfigured.Should().BeFalse();
        service.ValidateToken("bound", "10.0.0.1").Should().BeTrue();
        service.ValidateToken("bound", "10.0.0.2").Should().BeFalse();
    }

    // ---- IsAuthenticationEnabled is public surface: its answers are pinned, not changed -----------

    [Fact]
    public void IsAuthenticationEnabled_KeepsAllThreeOfItsAnswers()
    {
        // CONTRACT PIN, not evidence for the fix. Five transport wrappers expose this method, and
        // Birko.Communication.WebSocket.Tests already asserts these three values. The fix deliberately
        // left them alone and moved the GATE instead — this fails if someone "fixes" SH-H040 by
        // redefining what this method returns, which would silently change every wrapper's answer.
        using var disabled = Service(enabled: false, "token");
        using var enabledNoTokens = Service(enabled: true);
        using var enabledWithTokens = Service(enabled: true, "token");

        disabled.IsAuthenticationEnabled().Should().BeFalse();
        enabledNoTokens.IsAuthenticationEnabled().Should().BeFalse();
        enabledWithTokens.IsAuthenticationEnabled().Should().BeTrue();
    }

    [Fact]
    public void TheTwoNewProperties_AreNotTheSameQuestion()
    {
        // The whole defect was one question standing in for two, so assert they genuinely differ:
        // exactly one of the three states is "disabled", and a different one is "misconfigured".
        using var disabled = Service(enabled: false);
        using var misconfigured = Service(enabled: true);
        using var healthy = Service(enabled: true, "token");

        (disabled.IsAuthenticationDisabled, disabled.IsMisconfigured).Should().Be((true, false));
        (misconfigured.IsAuthenticationDisabled, misconfigured.IsMisconfigured).Should().Be((false, true));
        (healthy.IsAuthenticationDisabled, healthy.IsMisconfigured).Should().Be((false, false));
    }
}
