using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Communication.WebSocket.Middleware;
using Birko.Communication.WebSocket.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using FluentAssertions;
using Xunit;
using SysWebSocket = System.Net.WebSockets.WebSocket;

namespace Birko.Communication.WebSocket.Tests;

/// <summary>
/// TASK-536 regression: <c>requireAuthentication</c> defaults to <c>true</c>, but both mapping paths
/// (<c>WebSocketAuthenticationMiddleware</c> and the legacy <c>MapWebSocket</c>) ran the token check
/// only <c>if (authService != null)</c>. Nothing in the framework registers
/// <see cref="WebSocketAuthenticationService"/>, so the default accepted every anonymous upgrade.
/// A missing service now fails closed: mapping throws when the container can tell, and a request
/// that reaches the gate anyway gets 401 without the handler running.
/// </summary>
public class WebSocketEndpointAuthenticationTests
{
    private static WebSocketAuthenticationService NewAuthService(params string[] tokens) =>
        new(Options.Create(new WebSocketAuthenticationConfiguration { Enabled = true, Tokens = new List<string>(tokens) }),
            NullLogger<WebSocketAuthenticationService>.Instance,
            NullLogger<Birko.Security.Authentication.AuthenticationService>.Instance);

    private static IServiceProvider Services(WebSocketAuthenticationService? auth = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (auth != null)
        {
            services.AddSingleton(auth);
        }
        return services.BuildServiceProvider();
    }

    private static DefaultHttpContext UpgradeRequest(IServiceProvider services, string? token = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Features.Set<IHttpWebSocketFeature>(new FakeUpgradeFeature());
        context.Response.Body = new MemoryStream();
        if (token != null)
        {
            context.Request.QueryString = new QueryString($"?token={token}");
        }
        return context;
    }

    private static async Task<bool> RunMiddleware(DefaultHttpContext context, bool requireAuthentication)
    {
        var nextCalled = false;
        var middleware = new WebSocketAuthenticationMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            requireAuthentication,
            NullLogger<WebSocketAuthenticationMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return nextCalled;
    }

    // ---- middleware (MapWebSocketEndpoint's pipeline) ----

    [Fact]
    public async Task Middleware_RequireAuth_NoServiceRegistered_Rejects401()
    {
        var context = UpgradeRequest(Services());

        var passed = await RunMiddleware(context, requireAuthentication: true);

        passed.Should().BeFalse("an unconfigured auth requirement must not admit anonymous upgrades");
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Middleware_RequireAuth_InvalidToken_Rejects401()
    {
        var context = UpgradeRequest(Services(NewAuthService("secret")), token: "wrong");

        (await RunMiddleware(context, requireAuthentication: true)).Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task Middleware_RequireAuth_ValidToken_Passes()
    {
        var context = UpgradeRequest(Services(NewAuthService("secret")), token: "secret");

        (await RunMiddleware(context, requireAuthentication: true)).Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_AuthNotRequired_NoServiceRegistered_Passes()
    {
        var context = UpgradeRequest(Services());

        (await RunMiddleware(context, requireAuthentication: false)).Should().BeTrue();
    }

    // ---- map-time refusal ----

    [Fact]
    public void MapWebSocketEndpoint_RequireAuth_NoServiceRegistered_Throws()
    {
        var endpoints = new FakeEndpointRouteBuilder(Services());

        var act = () => endpoints.MapWebSocketEndpoint("/ws", (_, _) => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*WebSocketAuthenticationService*'/ws'*");
    }

    [Fact]
    public void MapWebSocketEndpoint_RequireAuth_ServiceRegistered_Maps()
    {
        var endpoints = new FakeEndpointRouteBuilder(Services(NewAuthService("secret")));

        var act = () => endpoints.MapWebSocketEndpoint("/ws", (_, _) => Task.CompletedTask);

        act.Should().NotThrow();
        endpoints.DataSources.SelectMany(d => d.Endpoints).Should().ContainSingle();
    }

    [Fact]
    public void MapWebSocketEndpointNoAuth_NoServiceRegistered_Maps()
    {
        var endpoints = new FakeEndpointRouteBuilder(Services());

        var act = () => endpoints.MapWebSocketEndpointNoAuth("/ws", (_, _) => Task.CompletedTask);

        act.Should().NotThrow();
    }

    // ---- legacy MapWebSocket ----

    [Fact]
    public void LegacyMapWebSocket_RequireAuth_NoServiceRegistered_Throws()
    {
        var app = new ApplicationBuilder(Services());

        var act = () => app.MapWebSocket("/ws", (_, _) => Task.CompletedTask);

        act.Should().Throw<InvalidOperationException>().WithMessage("*WebSocketAuthenticationService*");
    }

    [Fact]
    public async Task LegacyMapWebSocket_RequireAuth_ServiceRemovedAfterMapping_Rejects401()
    {
        // Mapping saw a registered service; the request scope does not. The per-request gate is the
        // backstop for containers that cannot answer at map time — it must fail closed too.
        var app = new ApplicationBuilder(Services(NewAuthService("secret")));
        var handlerRan = false;
        app.MapWebSocket("/ws", (_, _) => { handlerRan = true; return Task.CompletedTask; });
        var pipeline = app.Build();

        var context = UpgradeRequest(Services());
        context.Request.Path = "/ws";
        await pipeline(context);

        handlerRan.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task LegacyMapWebSocket_RequireAuth_InvalidToken_Rejects401()
    {
        var auth = NewAuthService("secret");
        var app = new ApplicationBuilder(Services(auth));
        var handlerRan = false;
        app.MapWebSocket("/ws", (_, _) => { handlerRan = true; return Task.CompletedTask; });
        var pipeline = app.Build();

        var context = UpgradeRequest(Services(auth), token: "wrong");
        context.Request.Path = "/ws";
        await pipeline(context);

        handlerRan.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private sealed class FakeUpgradeFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<SysWebSocket> AcceptAsync(WebSocketAcceptContext context) =>
            Task.FromResult<SysWebSocket>(SysWebSocket.CreateFromStream(new MemoryStream(), isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.Zero));
    }

    private sealed class FakeEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public FakeEndpointRouteBuilder(IServiceProvider services) => ServiceProvider = services;

        public IServiceProvider ServiceProvider { get; }
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
