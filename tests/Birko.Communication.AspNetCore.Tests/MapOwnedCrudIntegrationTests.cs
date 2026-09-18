using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Birko.Communication.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.Communication.AspNetCore.Tests;

/// <summary>
/// End-to-end tests for <c>MapOwnedCrud</c> over a real in-memory <see cref="TestServer"/>: proves the
/// route/verb wiring, JSON binding, per-request DI resolution, the <c>Created</c> location, and that
/// the owner-scoping guards fire over actual HTTP (foreign entities read as 404; create-clash → 409).
/// </summary>
public class MapOwnedCrudIntegrationTests
{
    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class Widget
    {
        public Guid Guid { get; set; }
        public Guid? Owner { get; set; }
        public string? Name { get; set; }
    }

    private sealed class WidgetRequest
    {
        public Guid? Guid { get; set; }
        public string? Name { get; set; }
    }

    private sealed class WidgetDto
    {
        public Guid Guid { get; set; }
        public string? Name { get; set; }
    }

    private sealed class FakeOwner
    {
        public Guid Id { get; set; }
    }

    // A minimal repository with the Read/Create/Update/Delete surface the mapping delegates call, plus
    // owner stamping on create (mirroring the real OwnedRepository).
    private sealed class FakeRepo
    {
        private readonly FakeOwner _owner;
        private readonly Dictionary<Guid, Widget> _store = new();

        public FakeRepo(FakeOwner owner) => _owner = owner;

        public void Seed(Widget w) => _store[w.Guid] = w;

        public Widget? Read(Guid id) => _store.TryGetValue(id, out var w) ? w : null;

        public Guid Create(Widget w)
        {
            if (w.Guid == Guid.Empty)
            {
                w.Guid = Guid.NewGuid();
            }
            w.Owner ??= _owner.Id;
            _store[w.Guid] = w;
            return w.Guid;
        }

        public void Update(Widget w) => _store[w.Guid] = w;

        public void Delete(Widget w) => _store.Remove(w.Guid);
    }

    private static OwnedCrudMapping<Widget, WidgetRequest, FakeRepo> Mapping() => new()
    {
        Owner = ctx => ctx.RequestServices.GetRequiredService<FakeOwner>().Id,
        Read = (repo, id) => repo.Read(id),
        Create = (repo, w) => repo.Create(w),
        Update = (repo, w) => repo.Update(w),
        Delete = (repo, w) => repo.Delete(w),
        OwnerOf = w => w.Owner,
        ToDto = w => new WidgetDto { Guid = w.Guid, Name = w.Name },
        Build = req => new Widget { Guid = req.Guid ?? Guid.Empty, Name = req.Name },
        Apply = (req, w) => w.Name = req.Name,
        RequestGuid = req => req.Guid,
        Label = "widget",
    };

    private static Task<IHost> StartHostAsync(FakeOwner owner) =>
        new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton(owner);
                    services.AddSingleton<FakeRepo>();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapOwnedCrud("/api/widgets", Mapping()));
                });
            })
            .StartAsync();

    [Fact]
    public async Task Full_lifecycle_and_owner_scoping_over_http()
    {
        using var host = await StartHostAsync(new FakeOwner { Id = Me });
        var client = host.GetTestClient();

        // A foreign-owned entity must read as 404 (not leaked).
        var foreignId = Guid.NewGuid();
        host.Services.GetRequiredService<FakeRepo>().Seed(new Widget { Guid = foreignId, Owner = Someone, Name = "theirs" });
        (await client.GetAsync($"/api/widgets/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // POST create -> 201 Created + Location.
        var post = await client.PostAsJsonAsync("/api/widgets", new WidgetRequest { Name = "mine" });
        post.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await post.Content.ReadFromJsonAsync<WidgetDto>();
        created!.Guid.Should().NotBe(Guid.Empty);
        post.Headers.Location!.ToString().Should().EndWith($"/api/widgets/{created.Guid}");

        // GET -> 200.
        var get = await client.GetFromJsonAsync<WidgetDto>($"/api/widgets/{created.Guid}");
        get!.Name.Should().Be("mine");

        // PUT -> 200 and the change persists.
        var put = await client.PutAsJsonAsync($"/api/widgets/{created.Guid}", new WidgetRequest { Name = "renamed" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetFromJsonAsync<WidgetDto>($"/api/widgets/{created.Guid}"))!.Name.Should().Be("renamed");

        // DELETE -> 204, then gone (404).
        (await client.DeleteAsync($"/api/widgets/{created.Guid}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/widgets/{created.Guid}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_with_clashing_id_returns_409_when_owned_and_404_when_foreign()
    {
        using var host = await StartHostAsync(new FakeOwner { Id = Me });
        var client = host.GetTestClient();
        var repo = host.Services.GetRequiredService<FakeRepo>();

        var ownedId = Guid.NewGuid();
        repo.Seed(new Widget { Guid = ownedId, Owner = Me, Name = "mine" });
        var clashOwned = await client.PostAsJsonAsync("/api/widgets", new WidgetRequest { Guid = ownedId, Name = "again" });
        clashOwned.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var foreignId = Guid.NewGuid();
        repo.Seed(new Widget { Guid = foreignId, Owner = Someone, Name = "theirs" });
        var clashForeign = await client.PostAsJsonAsync("/api/widgets", new WidgetRequest { Guid = foreignId, Name = "steal" });
        clashForeign.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
