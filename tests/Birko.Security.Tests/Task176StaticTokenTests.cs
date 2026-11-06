using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// TASK-176 — SH-L220 (static tokens were compared with ordinal equality, which returns at the first differing
/// character) and SH-L238 (the dispose flag was a plain bool read and written with no synchronisation).
/// </summary>
/// <remarks>
/// CONTRACT PINS, not evidence: both fixes pass these tests and so did the code before them. A comparison's timing is
/// not observable in a unit test, and a second ReaderWriterLockSlim.Dispose is harmless, so neither defect can be made
/// to fail here. What is pinned is that the fixed-time comparison decides exactly as the ordinal one did, including on
/// a prefix and on a same-length near miss, and that concurrent Dispose calls stay quiet.
/// </remarks>
public class Task176StaticTokenTests
{
    private sealed class Config : AuthenticationConfiguration { }

    private const string Token = "static-token-0123456789abcdef";

    private static AuthenticationService Service(bool binding = false) => new(new Config
    {
        Enabled = true,
        Tokens = binding ? new List<string>() : new List<string> { "other-token", Token },
        TokenBindings = binding ? new List<TokenBinding> { new() { Token = Token, AllowedIps = new List<string> { "10.0.0.1" } } } : new(),
    });

    [Theory]
    [InlineData(Token, true)]
    [InlineData("static-token-0123456789abcdeX", false)] // same length, last character differs
    [InlineData("static-token", false)] // a prefix
    [InlineData(Token + "x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_static_token_is_decided_exactly(string? presented, bool expected)
    {
        using var service = Service();
        service.ValidateToken(presented, "10.0.0.1").Should().Be(expected);
    }

    [Theory]
    [InlineData(Token, "10.0.0.1", true)]
    [InlineData(Token, "10.0.0.2", false)]
    [InlineData("static-token-0123456789abcdeX", "10.0.0.1", false)]
    public void A_bound_token_is_decided_exactly(string presented, string ip, bool expected)
    {
        using var service = Service(binding: true);
        service.ValidateToken(presented, ip).Should().Be(expected);
    }

    [Fact]
    public void Concurrent_Dispose_calls_are_quiet()
    {
        var service = Service();
        var act = () => Parallel.For(0, 64, _ => service.Dispose());
        act.Should().NotThrow();
    }
}
