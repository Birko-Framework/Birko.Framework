using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security;
using Birko.Security.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Birko.Security.Vault.Tests;

public class SecretConfigurationProviderTests
{
    private class MockSecretProvider : ISecretProvider
    {
        private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _pairs = new();
        private readonly Dictionary<string, IReadOnlyList<string>> _children = new();

        public MockSecretProvider AddPairs(string path, IReadOnlyDictionary<string, string> pairs)
        {
            _pairs[path] = pairs;
            return this;
        }

        public MockSecretProvider AddChildren(string path, params string[] children)
        {
            _children[path] = children;
            return this;
        }

        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
            => Task.FromResult<SecretResult?>(null);

        public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteSecretAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSecretsAsync(string? path, CancellationToken ct = default)
            => Task.FromResult(_children.GetValueOrDefault(path) ?? Array.Empty<string>());

        public Task<IReadOnlyDictionary<string, string>?> GetSecretPairsAsync(string key, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>?>(_pairs.GetValueOrDefault(key));
    }

    private class ThrowingListSecretProvider : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string key, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<SecretResult?> GetSecretWithMetadataAsync(string key, CancellationToken ct = default)
            => Task.FromResult<SecretResult?>(null);

        public Task SetSecretAsync(string key, string value, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteSecretAsync(string key, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListSecretsAsync(string? path, CancellationToken ct = default)
            => throw new HttpRequestException("connection refused");

        public Task<IReadOnlyDictionary<string, string>?> GetSecretPairsAsync(string key, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string> { ["name"] = "root" });
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var act = () => new SecretConfigurationProvider(null!, "path");
        act.Should().Throw<ArgumentNullException>().WithParameterName("provider");
    }

    [Fact]
    public void Load_SinglePath_LoadsKeyValuePairs()
    {
        var provider = new MockSecretProvider()
            .AddPairs("myapp/db", new Dictionary<string, string>
            {
                ["username"] = "admin",
                ["password"] = "secret"
            });

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "myapp/db", recursive: false)
            .Build();

        config["username"].Should().Be("admin");
        config["password"].Should().Be("secret");
    }

    [Fact]
    public void Load_DoubleDashKey_ConvertedToColonDelimiter()
    {
        var provider = new MockSecretProvider()
            .AddPairs("app", new Dictionary<string, string>
            {
                ["Security--DevCertificate--Fingerprint"] = "abc123"
            });

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "app", recursive: false)
            .Build();

        config["Security:DevCertificate:Fingerprint"].Should().Be("abc123");
    }

    [Fact]
    public void Load_NonExistentPath_SetsNothing()
    {
        var provider = new MockSecretProvider();
        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "nonexistent", recursive: false)
            .Build();

        config.AsEnumerable().Should().BeEmpty();
    }

    [Fact]
    public void Load_Recursive_ListsChildrenAndRecurses()
    {
        var provider = new MockSecretProvider()
            .AddPairs("myapp", new Dictionary<string, string> { ["name"] = "root" })
            .AddChildren("myapp", "db")
            .AddPairs("myapp/db", new Dictionary<string, string> { ["host"] = "localhost" })
            .AddChildren("myapp/db", "replica")
            .AddPairs("myapp/db/replica", new Dictionary<string, string> { ["host"] = "replica.local" });

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "myapp", recursive: true)
            .Build();

        config["name"].Should().Be("root");
        config["db:host"].Should().Be("localhost");
        config["db:replica:host"].Should().Be("replica.local");
    }

    [Fact]
    public void Load_NonRecursive_StopsAtCurrentPath()
    {
        var provider = new MockSecretProvider()
            .AddPairs("myapp", new Dictionary<string, string> { ["name"] = "root" })
            .AddChildren("myapp", "db")
            .AddPairs("myapp/db", new Dictionary<string, string> { ["host"] = "localhost" });

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "myapp", recursive: false)
            .Build();

        config["name"].Should().Be("root");
        config["myapp:db:host"].Should().BeNull();
    }

    [Fact]
    public void Load_ListSecretsThrows_GracefullyHandled()
    {
        var provider = new ThrowingListSecretProvider();

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, "myapp", recursive: true)
            .Build();

        config["name"].Should().Be("root");
    }

    [Fact]
    public void AddSecretConfiguration_NullBuilder_Throws()
    {
        var provider = new MockSecretProvider();
        var act = () => ((IConfigurationBuilder)null!).AddSecretConfiguration(provider, "path");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSecretConfiguration_NullProvider_Throws()
    {
        var builder = new ConfigurationBuilder();
        var act = () => builder.AddSecretConfiguration(null!, "path");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddSecretConfiguration_MultiplePaths_AddsSourcesInOrder()
    {
        var provider = new MockSecretProvider()
            .AddPairs("defaults", new Dictionary<string, string> { ["timeout"] = "30" })
            .AddPairs("production", new Dictionary<string, string> { ["timeout"] = "60", ["host"] = "prod.local" });

        var config = new ConfigurationBuilder()
            .AddSecretConfiguration(provider, new[] { "defaults", "production" }, recursive: false)
            .Build();

        config["timeout"].Should().Be("60");
        config["host"].Should().Be("prod.local");
    }
}
