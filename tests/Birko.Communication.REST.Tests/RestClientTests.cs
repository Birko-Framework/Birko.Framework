using Birko.Communication.REST;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.Communication.REST.Tests;

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
}
