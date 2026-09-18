using Birko.Communication.REST;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.Communication.REST.Tests;

// TASK-459: this class calls RestClient.ClearCache() and RestClient.GetClient(), which operate on a
// STATIC cache. RestClientCacheTests was already marked [Collection("RestClientCache")] with the
// comment "avoid interleaving with other tests that touch the static cache" — but xUnit serialises
// classes WITHIN a collection and runs different collections in PARALLEL, so naming the collection
// on one of the two classes protected nothing. ClearCache_EvictsAllEntries below wiped the cache in
// the middle of GetClient_ConcurrentAccess_DoesNotCorruptCache, which then saw exactly 80 distinct
// instances where it expected 40 — the same 40 URIs resolved twice, once either side of the
// eviction. It failed 2 of 4 CI runs and never once locally, because the interleaving needs the two
// classes to genuinely overlap.
// Both classes must name the collection, or neither is serialised against the other.
[Collection("RestClientCache")]
public class RestClientTests
{
    #region Constructor

    [Fact]
    public void Constructor_NullBaseUri_Throws()
    {
        var act = () => new RestClient(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("baseUri");
    }

    [Fact]
    public void Constructor_EmptyBaseUri_Throws()
    {
        var act = () => new RestClient("");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_TrimsTrailingSlash()
    {
        using var client = new RestClient("https://api.example.com/");

        client.BaseURI.Should().Be("https://api.example.com");
    }

    [Fact]
    public void Constructor_NoTrailingSlash_Unchanged()
    {
        using var client = new RestClient("https://api.example.com");

        client.BaseURI.Should().Be("https://api.example.com");
    }

    #endregion

    #region Defaults

    [Fact]
    public void DefaultContentType_IsJson()
    {
        using var client = new RestClient("https://api.example.com");

        client.DefaultContentType.Should().Be("application/json");
    }

    [Fact]
    public void DefaultHeaders_IsEmpty()
    {
        using var client = new RestClient("https://api.example.com");

        client.DefaultHeaders.Should().BeEmpty();
    }

    [Fact]
    public void Timeout_DefaultIs100Seconds()
    {
        using var client = new RestClient("https://api.example.com");

        client.Timeout.Should().Be(100000);
    }

    #endregion

    #region BuildUri — tested via reflection since protected

    // BuildUri is protected, but we can test its behavior indirectly through the event args.
    // Instead, let's test via a subclass.

    private class TestableRestClient : RestClient
    {
        public TestableRestClient(string baseUri) : base(baseUri) { }

        public string TestBuildUri(string endpoint, string? queryString)
            => BuildUri(endpoint, queryString);
    }

    [Fact]
    public void BuildUri_SimpleEndpoint_CombinesBaseAndEndpoint()
    {
        var client = new TestableRestClient("https://api.example.com");

        var uri = client.TestBuildUri("/users/1", null);

        uri.Should().Be("https://api.example.com/users/1");
    }

    [Fact]
    public void BuildUri_EndpointWithoutLeadingSlash_TrimsCorrectly()
    {
        var client = new TestableRestClient("https://api.example.com");

        var uri = client.TestBuildUri("users/1", null);

        uri.Should().Be("https://api.example.com/users/1");
    }

    [Fact]
    public void BuildUri_WithQueryString_AppendsQuestion()
    {
        var client = new TestableRestClient("https://api.example.com");

        var uri = client.TestBuildUri("/search", "q=test&limit=10");

        uri.Should().Be("https://api.example.com/search?q=test&limit=10");
    }

    [Fact]
    public void BuildUri_QueryStringAlreadyHasQuestion_DoesNotDouble()
    {
        var client = new TestableRestClient("https://api.example.com");

        var uri = client.TestBuildUri("/search", "?q=test");

        uri.Should().Be("https://api.example.com/search?q=test");
    }

    [Fact]
    public void BuildUri_NullQueryString_NoQuestionMark()
    {
        var client = new TestableRestClient("https://api.example.com");

        var uri = client.TestBuildUri("/items", null);

        uri.Should().NotContain("?");
    }

    #endregion

    #region Event Args

    [Fact]
    public void RestRequestEventArgs_SetsProperties()
    {
        var args = new RestRequestEventArgs("GET", "https://api.example.com/test", null);

        args.Method.Should().Be("GET");
        args.Uri.Should().Be("https://api.example.com/test");
        args.Body.Should().BeNull();
        args.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void RestResponseEventArgs_SetsProperties()
    {
        var args = new RestResponseEventArgs("POST", "https://api.example.com/test", "{}", System.Net.HttpStatusCode.OK);

        args.Method.Should().Be("POST");
        args.Uri.Should().Be("https://api.example.com/test");
        args.Content.Should().Be("{}");
        args.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    #endregion

    #region DefaultHeaders

    [Fact]
    public void DefaultHeaders_CanBeModified()
    {
        using var client = new RestClient("https://api.example.com");
        client.DefaultHeaders["Authorization"] = "Bearer token123";

        client.DefaultHeaders.Should().ContainKey("Authorization");
        client.DefaultHeaders["Authorization"].Should().Be("Bearer token123");
    }

    #endregion

    #region Static client cache (CR-L080)

    [Fact]
    public void GetClient_SameUri_ReturnsCachedInstance()
    {
        var uri = "https://cache-l080-a.example.com";
        try
        {
            var a = RestClient.GetClient(uri);
            var b = RestClient.GetClient(uri);
            a.Should().BeSameAs(b, "GetClient must cache by base URI");
            RestClient.GetClient("https://cache-l080-b.example.com").Should().NotBeSameAs(a);
        }
        finally
        {
            RestClient.RemoveClient(uri);
            RestClient.RemoveClient("https://cache-l080-b.example.com");
        }
    }

    [Fact]
    public void RemoveClient_EvictsAndReturnsFreshInstanceNextTime()
    {
        var uri = "https://cache-l080-remove.example.com";
        var first = RestClient.GetClient(uri);

        RestClient.RemoveClient(uri).Should().BeTrue();
        RestClient.RemoveClient(uri).Should().BeFalse("already removed");

        var second = RestClient.GetClient(uri);
        second.Should().NotBeSameAs(first, "a removed client must not be handed out again");
        RestClient.RemoveClient(uri);
    }

    [Fact]
    public void ClearCache_EvictsAllEntries()
    {
        var uri = "https://cache-l080-clear.example.com";
        var first = RestClient.GetClient(uri);

        RestClient.ClearCache();

        RestClient.GetClient(uri).Should().NotBeSameAs(first);
        RestClient.RemoveClient(uri);
    }

    #endregion
}
