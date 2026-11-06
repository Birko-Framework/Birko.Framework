using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.Vault;
using Birko.Security.Vault.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Birko.Security.Vault.Tests;

/// <summary>
/// TASK-176 — the Vault low findings: SH-L231 (resolving the options rewrote the caller's instance), SH-L232 (the client
/// AddLocalVaultConfiguration made was never disposed), SH-L233 (a KvVersion other than 1 or 2 was silently treated as 1)
/// and SH-L234 (IsHealthyAsync answered "unhealthy" to the caller's own cancellation).
/// </summary>
public class Task176VaultTests
{
    private static VaultSettings Settings(int kv = 2) => new() { Address = "http://localhost:8200", Token = "t", KvVersion = kv };

    private sealed class Handler(Func<CancellationToken, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(answer(ct));
        }
    }

    // ── SH-L233 ──

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_KvVersion_other_than_1_or_2_is_refused(int kv) =>
        FluentActions.Invoking(() => new VaultSecretProvider(Settings(kv))).Should().Throw<ArgumentOutOfRangeException>().WithMessage("*KvVersion*");

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void KvVersion_1_and_2_are_accepted(int kv) // contract pin
    {
        using var client = new VaultSecretProvider(Settings(kv));
        client.Should().NotBeNull();
    }

    // ── SH-L234 ──

    [Fact]
    public async Task The_callers_cancellation_propagates_from_IsHealthyAsync()
    {
        using var client = new VaultSecretProvider(Settings(), new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => client.IsHealthyAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_timeout_is_still_an_unhealthy_answer() // contract pin: only the CALLER's cancellation propagates
    {
        using var client = new VaultSecretProvider(Settings(), new HttpClient(new Handler(_ => throw new TaskCanceledException("timeout"))));

        (await client.IsHealthyAsync()).Should().BeFalse();
    }

    // ── SH-L231 ──

    [Fact]
    public void Resolving_the_options_leaves_the_callers_instance_alone()
    {
        var seed = new LocalVaultOptions { Token = "t", Url = "http://vault", User = "MiXed", Domain = "DOM", Environment = "Prod" };

        var resolved = LocalVaultConfigurationExtensions.ResolveOptions(seed);

        resolved.Should().NotBeSameAs(seed);
        resolved.User.Should().Be("mixed");
        seed.User.Should().Be("MiXed");
        seed.Domain.Should().Be("DOM");
        seed.Environment.Should().Be("Prod");
    }

    // ── SH-L232 ──

    private static VaultSecretProvider ClientOf(IConfigurationProvider provider) =>
        (VaultSecretProvider)typeof(LocalVaultConfigurationProvider)
            .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(provider)!;

    [Fact]
    public async Task A_provider_built_from_settings_disposes_the_client_it_made()
    {
        var provider = new LocalVaultConfigurationSource(Settings(), "app").Build(new ConfigurationBuilder());
        var client = ClientOf(provider);

        ((IDisposable)provider).Dispose();

        var act = () => client.GetSecretAsync("k");
        await act.Should().ThrowAsync<ObjectDisposedException>("the provider owned the client and disposed it");
    }

    [Fact]
    public async Task A_provider_over_a_callers_client_leaves_it_open()
    {
        using var client = new VaultSecretProvider(Settings(), new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var provider = new LocalVaultConfigurationSource(client, "app").Build(new ConfigurationBuilder());

        ((IDisposable)provider).Dispose();

        (await client.IsHealthyAsync()).Should().BeTrue("the caller made the client, so the caller disposes it");
    }
}
