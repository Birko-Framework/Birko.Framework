using System;
using Birko.Configuration;
using Birko.Data.Models;
using Birko.Data.Repositories;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Repositories.Tests;

/// <summary>
/// CR-L172: the settings-keyed GetRepository&lt;TRepository, TSettings&gt; overload constructs the repository
/// parameterlessly and uses the settings only as the cache key. A repository whose only constructor takes a
/// store now fails with a clear InvalidOperationException instead of a raw MissingMethodException; a
/// repository with a parameterless constructor is created and cached by the settings id.
/// </summary>
public class RepositoryLocatorSettingsOverloadTests
{
    private class Entity : AbstractModel { }

    // Only a store-taking constructor — no public parameterless ctor.
    private sealed class StoreOnlyRepository : AbstractBulkRepository<Entity>
    {
        public StoreOnlyRepository(Stores.IBulkStore<Entity>? store) : base(store) { }
    }

    // Has a public parameterless constructor (self-provisions a null store here).
    private sealed class ParameterlessRepository : AbstractBulkRepository<Entity>
    {
        public ParameterlessRepository() : base(null) { }
    }

    [Fact]
    public void Settings_overload_on_a_store_only_repository_throws_a_clear_error()
    {
        var settings = new Settings { Name = "svc", Location = "l1" };

        Action act = () => RepositoryLocator.GetRepository<StoreOnlyRepository, Settings>(settings);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*parameterless constructor*");
    }

    [Fact]
    public void Settings_overload_creates_and_caches_a_parameterless_repository()
    {
        var settings = new Settings { Name = "svc", Location = "cacheL2" };

        var first = RepositoryLocator.GetRepository<ParameterlessRepository, Settings>(settings);
        var second = RepositoryLocator.GetRepository<ParameterlessRepository, Settings>(settings);

        first.Should().NotBeNull();
        second.Should().BeSameAs(first, "the repository is cached by the settings id");

        RepositoryLocator.Destroy<ParameterlessRepository, Settings>(settings);
    }
}
