using System;
using Birko.Communication.SOAP.Middleware;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SOAP.Tests;

/// <summary>
/// TASK-709. Measured before the fix: <c>SoapAuthenticationService</c> had a <c>Dispose()</c> but was not
/// <see cref="IDisposable"/>, so neither <c>using</c> nor a DI container disposed it.
/// </summary>
public class Task709SoapAuthenticationTests
{
    [Fact]
    public void The_service_is_disposable()
    {
        new SoapAuthenticationService(new SoapAuthenticationConfiguration()).Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void The_query_token_name_matches_exactly()
    {
        using var service = (IDisposable)new SoapAuthenticationService(new SoapAuthenticationConfiguration());
        var soap = (SoapAuthenticationService)service;

        soap.ExtractTokenFromQueryString("?token=abc==").Should().Be("abc==");
        soap.ExtractTokenFromQueryString("?Token=abc").Should().BeNull();
    }
}
