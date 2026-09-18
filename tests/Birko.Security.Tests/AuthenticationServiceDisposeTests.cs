using System;
using Birko.Security.Authentication;
using FluentAssertions;
using Xunit;

namespace Birko.Security.Tests;

/// <summary>
/// CR-H136: AuthenticationService owns a ReaderWriterLockSlim and defines Dispose(), but did not
/// implement IDisposable — so DI containers and `using` never invoked it and the lock leaked.
/// </summary>
public class AuthenticationServiceDisposeTests
{
    private sealed class TestConfig : AuthenticationConfiguration
    {
    }

    private static AuthenticationService Create() => new(new TestConfig { Enabled = false });

    [Fact]
    public void AuthenticationService_ImplementsIDisposable()
    {
        Create().Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var service = Create();

        var act = () =>
        {
            service.Dispose();
            service.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public void CanBeUsedInUsingStatement()
    {
        var act = () =>
        {
            using var service = Create();
        };

        act.Should().NotThrow();
    }
}
