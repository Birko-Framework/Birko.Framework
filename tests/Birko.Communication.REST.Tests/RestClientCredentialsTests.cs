using System.Net;
using System.Net.Http;
using System.Reflection;
using Birko.Communication.REST;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.REST.Tests;

/// <summary>
/// Regression for CR-M059: the Credentials property was a plain auto-property read nowhere, so
/// setting it silently did nothing. It is now backed by the underlying HttpClientHandler so it
/// actually authenticates requests.
/// </summary>
public class RestClientCredentialsTests
{
    private static HttpClientHandler GetHandler(RestClient client)
    {
        var field = typeof(RestClient).GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (HttpClientHandler)field.GetValue(client)!;
    }

    [Fact]
    public void Credentials_FlowToTheUnderlyingHandler()
    {
        using var client = new RestClient("https://api.example.com");
        var creds = new NetworkCredential("user", "pass");

        client.Credentials = creds;

        client.Credentials.Should().BeSameAs(creds, "the getter reads back from the handler");
        GetHandler(client).Credentials.Should().BeSameAs(creds, "credentials must reach the handler that sends requests");
    }

    [Fact]
    public void Credentials_DefaultNull()
    {
        using var client = new RestClient("https://api.example.com");
        client.Credentials.Should().BeNull();
    }
}
