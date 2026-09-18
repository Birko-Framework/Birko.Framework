using System;
using Birko.Communication.REST.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.REST.Server.Tests;

/// <summary>
/// Regression for CR-M062: RestAuthenticationService declared a public Dispose() (disposing the
/// inner AuthenticationService, owner of a ReaderWriterLockSlim) but did not implement IDisposable,
/// so `using` and generic dispose patterns never called it and the lock leaked.
/// </summary>
public class RestAuthenticationServiceDisposableTests
{
    [Fact]
    public void RestAuthenticationService_ImplementsIDisposable()
    {
        typeof(RestAuthenticationService).Should().BeAssignableTo<IDisposable>();
    }
}
