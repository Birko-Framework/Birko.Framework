using System;
using Birko.Communication.AspNetCore;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Birko.Communication.AspNetCore.Tests;

/// <summary>
/// Unit tests for the owner-scoping guard core (<see cref="OwnedCrudResults"/>) — the error-prone
/// logic behind every owned-CRUD endpoint. Inspects the returned <see cref="IResult"/> via the public
/// <see cref="IStatusCodeHttpResult"/>/<see cref="IValueHttpResult"/> interfaces, so no web host is
/// needed (matching the framework's host-free ASP.NET test style).
/// </summary>
public class OwnedCrudResultsTests
{
    private sealed class Doc
    {
        public Guid Id { get; set; }
        public Guid? Owner { get; set; }
        public string? Name { get; set; }
    }

    private static readonly Guid Me = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Someone = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static int? Status(IResult? r) => (r as IStatusCodeHttpResult)?.StatusCode;
    private static object? Value(IResult? r) => (r as IValueHttpResult)?.Value;

    private static Doc Owned() => new() { Id = Guid.NewGuid(), Owner = Me, Name = "mine" };

    // ---- ReadOwned (GET /{id}) ----

    [Fact]
    public void ReadOwned_absent_is_404()
    {
        Status(OwnedCrudResults.ReadOwned<Doc>(null, Me, d => d.Owner, d => d)).Should().Be(404);
    }

    [Fact]
    public void ReadOwned_foreign_is_404()
    {
        var foreign = new Doc { Owner = Someone };
        Status(OwnedCrudResults.ReadOwned(foreign, Me, d => d.Owner, d => d)).Should().Be(404);
    }

    [Fact]
    public void ReadOwned_owned_is_200_with_dto()
    {
        var doc = Owned();
        var dto = new { doc.Name };
        var result = OwnedCrudResults.ReadOwned(doc, Me, d => d.Owner, _ => dto);
        Status(result).Should().Be(200);
        Value(result).Should().BeSameAs(dto);
    }

    // ---- CreateClash (POST with client-supplied id) ----

    [Fact]
    public void CreateClash_no_existing_proceeds()
    {
        OwnedCrudResults.CreateClash<Doc>(null, Me, d => d.Owner, "doc").Should().BeNull();
    }

    [Fact]
    public void CreateClash_owned_existing_is_409()
    {
        Status(OwnedCrudResults.CreateClash(Owned(), Me, d => d.Owner, "doc")).Should().Be(409);
    }

    [Fact]
    public void CreateClash_foreign_existing_is_404_not_409()
    {
        // Must not leak a foreign entity's existence via 409.
        var foreign = new Doc { Owner = Someone };
        Status(OwnedCrudResults.CreateClash(foreign, Me, d => d.Owner, "doc")).Should().Be(404);
    }

    // ---- RequireOwned (PUT/DELETE guard) ----

    [Fact]
    public void RequireOwned_owned_yields_entity_and_no_result()
    {
        var doc = Owned();
        var guard = OwnedCrudResults.RequireOwned(doc, Me, d => d.Owner, out var owned);
        guard.Should().BeNull();
        owned.Should().BeSameAs(doc);
    }

    [Fact]
    public void RequireOwned_absent_is_404()
    {
        var guard = OwnedCrudResults.RequireOwned<Doc>(null, Me, d => d.Owner, out var owned);
        Status(guard).Should().Be(404);
        owned.Should().BeNull();
    }

    [Fact]
    public void RequireOwned_foreign_is_404()
    {
        var foreign = new Doc { Owner = Someone };
        var guard = OwnedCrudResults.RequireOwned(foreign, Me, d => d.Owner, out var owned);
        Status(guard).Should().Be(404);
        owned.Should().BeNull();
    }
}
