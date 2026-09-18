using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Birko.Communication.SOAP;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.SOAP.Tests;

/// <summary>
/// Regression for CR-H033: CreateRequest received an ICredentials parameter (and SoapClient exposes
/// a Credentials property) but never applied either, so authenticated SOAP calls went out
/// unauthenticated. Credentials are now applied per-request as a Basic Authorization header.
/// </summary>
public class SoapClientCredentialsTests
{
    /// <summary>Test subclass exposing the protected CreateRequest.</summary>
    private sealed class TestableSoapClient : SoapClient
    {
        public TestableSoapClient(string uri) : base(uri) { }
        public HttpRequestMessage Build(string action, string xml, ICredentials? creds = null) => CreateRequest(action, xml, creds);
    }

    private static string? BasicToken(HttpRequestMessage req)
    {
        var auth = req.Headers.Authorization;
        return auth?.Scheme == "Basic" ? auth.Parameter : null;
    }

    [Fact]
    public void CreateRequest_AppliesCredentialParameter_AsBasicAuth()
    {
        using var client = new TestableSoapClient("https://soap.example.com/svc");

        using var request = client.Build("DoThing", "<x/>", new NetworkCredential("alice", "s3cret"));

        var token = BasicToken(request);
        token.Should().NotBeNull();
        Encoding.UTF8.GetString(Convert.FromBase64String(token!)).Should().Be("alice:s3cret");
    }

    [Fact]
    public void CreateRequest_AppliesCredentialsProperty_WhenNoParameter()
    {
        using var client = new TestableSoapClient("https://soap.example.com/svc")
        {
            Credentials = new NetworkCredential("bob", "pw")
        };

        using var request = client.Build("DoThing", "<x/>");

        Encoding.UTF8.GetString(Convert.FromBase64String(BasicToken(request)!)).Should().Be("bob:pw");
    }

    [Fact]
    public void CreateRequest_NoCredentials_HasNoAuthHeader()
    {
        using var client = new TestableSoapClient("https://soap.example.com/svc");

        using var request = client.Build("DoThing", "<x/>");

        request.Headers.Authorization.Should().BeNull();
    }
}
