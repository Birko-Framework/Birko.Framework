using Birko.Security.AspNetCore;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.AspNetCore;
using Birko.Security.Grants.AspNetCore.Authorization;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests.AspNetCore;

public class WiringTests
{
    private readonly InMemoryResourceGrantStore _grants = new();
    private readonly InMemoryInvitationStore _invitations = new();

    private IServiceCollection Services(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResourceGrantStore>(_grants);
        services.AddSingleton<IInvitationStore>(_invitations);
        extra?.Invoke(services);
        return services;
    }

    private static void Roles(GrantOptions o) => GrantsFixture.Configure(o);

    [Fact]
    public void AddBirkoGrants_RegistersEveryService()
    {
        var services = Services();
        services.AddBirkoGrants(Roles);
        using var scope = services.BuildServiceProvider().CreateScope();

        scope.ServiceProvider.GetRequiredService<IGrantService>().Should().BeOfType<GrantService>();
        scope.ServiceProvider.GetRequiredService<IInvitationService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAdminGrantService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IGrantSignInService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<GrantRoleTable>().Roles.Should().Contain("owner");
    }

    [Fact]
    public void AddBirkoGrants_AnInvalidTable_FailsAtRegistration()
    {
        var add = () => new ServiceCollection().AddBirkoGrants(o => o.DefineRole("owner", "read"));

        add.Should().Throw<GrantConfigurationException>();
    }

    [Fact]
    public void WithoutAStore_ResolvingFails_NamingIt()
    {
        var services = new ServiceCollection();
        services.AddBirkoGrants(Roles);
        using var scope = services.BuildServiceProvider().CreateScope();

        var resolve = () => scope.ServiceProvider.GetRequiredService<IGrantService>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*IResourceGrantStore*");
    }

    [Fact]
    public void WithGrantPermissions_WithoutAHostResolver_Throws()
    {
        var builder = Services().AddBirkoGrants(Roles);

        var wire = () => builder.WithGrantPermissions();

        wire.Should().Throw<InvalidOperationException>().WithMessage("*IUserPermissionResolver*");
    }

    [Fact]
    public void WithGrantPermissions_Twice_Throws()
    {
        var host = new FixedPermissionResolver();
        var builder = Services(s => s.AddSingleton<IUserPermissionResolver>(host)).AddBirkoGrants(Roles).WithGrantPermissions();

        var again = () => builder.WithGrantPermissions();

        again.Should().Throw<InvalidOperationException>().WithMessage("*already*");
    }

    [Theory]
    [InlineData("instance")]
    [InlineData("factory")]
    [InlineData("type")]
    public async Task WithGrantPermissions_DecoratesTheHostResolver_HoweverItWasRegistered(string how)
    {
        var services = Services(s =>
        {
            switch (how)
            {
                case "instance": s.AddSingleton<IUserPermissionResolver>(new FixedPermissionResolver()); break;
                case "factory": s.AddScoped<IUserPermissionResolver>(_ => new FixedPermissionResolver()); break;
                default: s.AddScoped<IUserPermissionResolver, FixedPermissionResolver>(); break;
            }
        });
        services.AddBirkoGrants(Roles).WithGrantPermissions();
        var admin = Guid.NewGuid();
        await _grants.CreateAsync(new Models.ResourceGrant { UserGuid = admin, ResourceType = "*", Role = "*" });
        using var scope = services.BuildServiceProvider().CreateScope();

        var resolver = scope.ServiceProvider.GetRequiredService<IUserPermissionResolver>();

        resolver.Should().BeOfType<GrantAwareUserPermissionResolver>();
        (await resolver.GetAsync(admin, null)).Should().Contain("*");
        (await resolver.GetRolesAsync(admin, null)).Should().Contain("host-role");
    }

    [Fact]
    public async Task WithGrantPermissions_MakesTheHostsSuperadminsGrantAdmins()
    {
        var superadmin = Guid.NewGuid();
        var services = Services(s => s.AddSingleton<IUserPermissionResolver>(new FixedPermissionResolver().Give(superadmin, "*")));
        services.AddBirkoGrants(Roles).WithGrantPermissions();
        using var scope = services.BuildServiceProvider().CreateScope();

        var grants = scope.ServiceProvider.GetRequiredService<IGrantService>();

        (await grants.CanAsync(superadmin, Draft, Guid.NewGuid(), "delete")).Should().BeTrue();
        (await grants.CanAsync(Guid.NewGuid(), Draft, Guid.NewGuid(), "read")).Should().BeFalse();
    }

    [Fact]
    public async Task RequireGrant_OnARealEndpoint_RunsThroughTheFactory()
    {
        var owner = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await _grants.CreateAsync(new Models.ResourceGrant { UserGuid = owner, ResourceType = Presentation, ResourceId = resource, Role = "owner" });

        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IResourceGrantStore>(_grants);
        builder.Services.AddSingleton<IInvitationStore>(_invitations);
        builder.Services.AddBirkoGrants(Roles);
        var app = builder.Build();
        app.MapPut("/presentations/{id}", () => "ran").RequireGrant(Presentation, "edit");
        var endpoint = (RouteEndpoint)((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).Single();

        async Task<int> PutAsync(Guid user, string id)
        {
            using var scope = app.Services.CreateScope();
            var http = new DefaultHttpContext { RequestServices = new ScopedWithUser(scope.ServiceProvider, new TestCurrentUser(true, user)) };
            http.Request.RouteValues["id"] = id;
            http.Response.Body = new MemoryStream();
            await endpoint.RequestDelegate!(http);
            return http.Response.StatusCode;
        }

        (await PutAsync(owner, resource.ToString())).Should().Be(200);
        (await PutAsync(Guid.NewGuid(), resource.ToString())).Should().Be(403);
        (await PutAsync(owner, "nope")).Should().Be(404);
    }

    [Fact]
    public void RequireGrant_WithAnUndeclaredAction_FailsWhenEndpointsAreBuilt()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IResourceGrantStore>(_grants);
        builder.Services.AddSingleton<IInvitationStore>(_invitations);
        builder.Services.AddBirkoGrants(Roles);
        var app = builder.Build();
        app.MapPut("/presentations/{id}", () => "ran").RequireGrant(Presentation, "publish");

        var build = () => ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();

        build.Should().Throw<GrantConfigurationException>();
    }

    [Fact]
    public void WithGrantPermissions_RefusesToReplaceTheHostsOwnAdminSource()
    {
        var services = Services(s =>
        {
            s.AddSingleton<IUserPermissionResolver>(new FixedPermissionResolver());
            s.AddScoped<IGrantAdminSource>(_ => new BootstrapAdminGrantSource(_grants));
        });

        var wire = () => services.AddBirkoGrants(Roles).WithGrantPermissions();

        wire.Should().Throw<InvalidOperationException>().WithMessage("*IGrantAdminSource*");
    }

    [Fact]
    public void WithGrantPermissions_IgnoresKeyedResolvers()
    {
        var builder = Services(s => s.AddKeyedSingleton<IUserPermissionResolver>("other", new FixedPermissionResolver())).AddBirkoGrants(Roles);

        var wire = () => builder.WithGrantPermissions();

        wire.Should().Throw<InvalidOperationException>().WithMessage("*none is registered*");
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    public void WithGrantPermissions_KeepsTheHostResolversLifetime(ServiceLifetime lifetime)
    {
        var services = Services(s => s.Add(new ServiceDescriptor(typeof(IUserPermissionResolver), typeof(FixedPermissionResolver), lifetime)));
        services.AddBirkoGrants(Roles).WithGrantPermissions();

        services.Single(d => d.ServiceType == typeof(IUserPermissionResolver)).Lifetime.Should().Be(lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserPermissionResolver>().Should().BeOfType<GrantAwareUserPermissionResolver>();
    }

    [Fact]
    public void WithGrantPermissions_DisposesAResolverItBuilt_ButNotAnInstanceTheHostOwns()
    {
        var owned = new DisposableResolver();
        var hostOwned = new DisposableResolver();
        var built = Services(s => s.AddScoped<IUserPermissionResolver>(_ => owned));
        built.AddBirkoGrants(Roles).WithGrantPermissions();
        var instance = Services(s => s.AddSingleton<IUserPermissionResolver>(hostOwned));
        instance.AddBirkoGrants(Roles).WithGrantPermissions();

        using (var scope = built.BuildServiceProvider().CreateScope())
            scope.ServiceProvider.GetRequiredService<IUserPermissionResolver>();
        using (var provider = instance.BuildServiceProvider())
        using (var scope = provider.CreateScope())
            scope.ServiceProvider.GetRequiredService<IUserPermissionResolver>();

        owned.Disposed.Should().BeTrue();
        hostOwned.Disposed.Should().BeFalse();
    }

    [Theory]
    [InlineData("/presentations")]
    [InlineData("/presentations/{id?}")]
    [InlineData("/presentations/{key}")]
    public void RequireGrant_OnARouteWithoutARequiredIdParameter_FailsWhenEndpointsAreBuilt(string pattern)
    {
        var app = App();
        app.MapPut(pattern, () => "ran").RequireGrant(Presentation, "edit");

        var build = () => ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();

        build.Should().Throw<InvalidOperationException>().WithMessage("*'id'*");
    }

    [Fact]
    public async Task ConfiguredReadAction_ReachesTheFilter_AndBothNotFoundsAreIdentical()
    {
        var viewer = Guid.NewGuid();
        var resource = Guid.NewGuid();
        await _grants.CreateAsync(new Models.ResourceGrant { UserGuid = viewer, ResourceType = Draft, ResourceId = resource, Role = "viewer" });
        var app = App(http => http.ReadAction = "edit");   // "may see" now means "may edit"
        app.MapDelete("/drafts/{id}", () => "ran").RequireGrant(Draft, "delete");
        var endpoint = (RouteEndpoint)((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).Single();

        var (unreadable, unreadableBody) = await SendAsync(app, endpoint, viewer, resource.ToString());
        var (missing, missingBody) = await SendAsync(app, endpoint, viewer, Guid.NewGuid().ToString());

        // A viewer cannot "edit", so under ReadAction = "edit" the draft is not visible to them: 404, not 403.
        unreadable.Should().Be(404);
        missing.Should().Be(404);
        unreadableBody.Should().Equal(missingBody);
    }

    private WebApplication App(Action<GrantsAspNetCoreOptions>? http = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IResourceGrantStore>(_grants);
        builder.Services.AddSingleton<IInvitationStore>(_invitations);
        builder.Services.AddBirkoGrants(Roles, configureHttp: http);
        return builder.Build();
    }

    private static async Task<(int Status, byte[] Body)> SendAsync(WebApplication app, RouteEndpoint endpoint, Guid user, string id)
    {
        using var scope = app.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = new ScopedWithUser(scope.ServiceProvider, new TestCurrentUser(true, user)) };
        http.Request.RouteValues["id"] = id;
        var body = new MemoryStream();
        http.Response.Body = body;
        await endpoint.RequestDelegate!(http);
        return (http.Response.StatusCode, body.ToArray());
    }

    private sealed class DisposableResolver : IUserPermissionResolver, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public Task<IReadOnlySet<string>> GetAsync(Guid userId, Guid? tenantId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
    }

    /// <summary>The request's services, with the test's <see cref="ICurrentUser"/> in front.</summary>
    private sealed class ScopedWithUser : IServiceProvider
    {
        private readonly IServiceProvider _inner;
        private readonly ICurrentUser _user;

        public ScopedWithUser(IServiceProvider inner, ICurrentUser user)
        {
            _inner = inner;
            _user = user;
        }

        public object? GetService(Type serviceType) => serviceType == typeof(ICurrentUser) ? _user : _inner.GetService(serviceType);
    }
}
