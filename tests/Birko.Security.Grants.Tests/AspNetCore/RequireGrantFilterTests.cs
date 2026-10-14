using Birko.Security.AspNetCore;
using Birko.Security.Grants.AspNetCore;
using Birko.Security.Grants.AspNetCore.Authorization;
using Birko.Security.Grants.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Birko.Security.Grants.Tests.AspNetCore.ResultAssertions;
using static Birko.Security.Grants.Tests.GrantsFixture;

namespace Birko.Security.Grants.Tests.AspNetCore;

public class RequireGrantFilterTests
{
    private readonly GrantsFixture _f = new();
    private readonly Guid _user = Guid.NewGuid();
    private readonly Guid _resource = Guid.NewGuid();

    private RequireGrantEndpointFilter Filter(string type, string action = "edit", string? routeParam = "id")
        => new(_f.Table, new GrantsAspNetCoreOptions(), type, action, routeParam);

    private async Task<int> InvokeAsync(RequireGrantEndpointFilter filter, ICurrentUser? user, object? routeValue)
    {
        var services = new ServiceCollection();
        if (user != null) services.AddSingleton(user);
        services.AddSingleton<IGrantService>(_f.Service);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (routeValue != null) http.Request.RouteValues["id"] = routeValue;
        var context = new DefaultEndpointFilterInvocationContext(http);
        return StatusOf(await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("ran")));
    }

    private ICurrentUser SignedIn(Guid? tenant = null) => new TestCurrentUser(true, _user, tenant);

    [Fact]
    public async Task Unauthenticated_Is401()
    {
        (await InvokeAsync(Filter(Presentation), new TestCurrentUser(false), _resource)).Should().Be(401);
        (await InvokeAsync(Filter(Presentation), null, _resource)).Should().Be(401);
    }

    [Fact]
    public async Task AuthenticatedWithoutALocalUserGuid_Is401_UnmappedSubject()
    {
        var filter = Filter(Presentation);
        var services = new ServiceCollection().AddSingleton<ICurrentUser>(new TestCurrentUser(true, userGuid: null))
            .AddSingleton<IGrantService>(_f.Service).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.RouteValues["id"] = _resource;

        var result = await filter.InvokeAsync(new DefaultEndpointFilterInvocationContext(http), _ => ValueTask.FromResult<object?>("ran"));

        StatusOf(result).Should().Be(401);
        result.Should().BeAssignableTo<IValueHttpResult>().Which.Value!.ToString().Should().Contain(RequireGrantEndpointFilter.UnmappedSubject);
    }

    [Fact]
    public async Task UnparseableId_Is404()
    {
        (await InvokeAsync(Filter(Presentation), SignedIn(), "not-a-guid")).Should().Be(404);
    }

    [Fact]
    public async Task MissingRouteValue_IsAConfigurationError()
    {
        var invoke = () => InvokeAsync(Filter(Presentation), SignedIn(), routeValue: null);

        await invoke.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'id'*");
    }

    [Fact]
    public async Task Allowed_RunsTheEndpoint()
    {
        await _f.SeedAsync(_user, Presentation, _resource, "editor");

        (await InvokeAsync(Filter(Presentation), SignedIn(), _resource.ToString())).Should().Be(200);
    }

    [Fact]
    public async Task DeniedOnAPublicType_Is403()
    {
        (await InvokeAsync(Filter(Presentation), SignedIn(), _resource)).Should().Be(403);
    }

    [Fact]
    public async Task DeniedOnAGrantedType_WithoutRead_Is404_LikeAMissingResource()
    {
        (await InvokeAsync(Filter(Draft), SignedIn(), _resource)).Should().Be(404);
        (await InvokeAsync(Filter(Draft), SignedIn(), Guid.NewGuid())).Should().Be(404);
    }

    [Fact]
    public async Task DeniedOnAGrantedType_WithRead_Is403()
    {
        await _f.SeedAsync(_user, Draft, _resource, "viewer");

        (await InvokeAsync(Filter(Draft), SignedIn(), _resource)).Should().Be(403);
    }

    [Fact]
    public async Task TypeWideCheck_UsesNoRouteValue_AndDeniesWith403()
    {
        (await InvokeAsync(Filter(Draft, "edit", routeParam: null), SignedIn(), routeValue: null)).Should().Be(403);

        await _f.SeedAsync(_user, Draft, null, "editor");
        (await InvokeAsync(Filter(Draft, "edit", routeParam: null), SignedIn(), routeValue: null)).Should().Be(200);
    }

    [Fact]
    public async Task TheCallersTenant_ScopesTheCheck()
    {
        var tenant = Guid.NewGuid();
        await _f.SeedAsync(_user, Presentation, _resource, "editor", tenant);

        (await InvokeAsync(Filter(Presentation), SignedIn(tenant), _resource)).Should().Be(200);
        (await InvokeAsync(Filter(Presentation), SignedIn(Guid.NewGuid()), _resource)).Should().Be(403);
    }

    [Fact]
    public async Task AnEmptyTenantGuid_MeansNoTenant()
    {
        await _f.SeedAsync(_user, Presentation, _resource, "editor");

        (await InvokeAsync(Filter(Presentation), SignedIn(Guid.Empty), _resource)).Should().Be(200);
    }

    [Fact]
    public void UndeclaredTypeActionOrReadAction_RefuseWhenTheFilterIsBuilt()
    {
        var badType = () => Filter("invoice");
        var badAction = () => Filter(Presentation, "publish");
        var badRead = () => new RequireGrantEndpointFilter(_f.Table, new GrantsAspNetCoreOptions { ReadAction = "view" }, Presentation, "edit", "id");

        badType.Should().Throw<GrantConfigurationException>();
        badAction.Should().Throw<GrantConfigurationException>();
        badRead.Should().Throw<GrantConfigurationException>();
    }
}
