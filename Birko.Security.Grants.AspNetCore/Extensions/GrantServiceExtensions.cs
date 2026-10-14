using System;
using System.Linq;
using Birko.Security.AspNetCore.Authorization;
using Birko.Security.Grants.AspNetCore.Authorization;
using Birko.Security.Grants.Services;
using Birko.Security.Grants.Stores;
using Birko.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Birko.Security.Grants.AspNetCore;

/// <summary>Returned by <see cref="GrantServiceExtensions.AddBirkoGrants"/>, for the optional wiring that follows it.</summary>
public sealed class BirkoGrantsBuilder
{
    internal BirkoGrantsBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }
}

/// <summary>DI registration for <c>Birko.Security.Grants</c>.</summary>
public static class GrantServiceExtensions
{
    /// <summary>
    /// Builds the role table from <paramref name="configure"/> (now — an invalid table fails at startup) and registers it with
    /// <see cref="IGrantService"/>, <see cref="IInvitationService"/>, <see cref="IAdminGrantService"/> and
    /// <see cref="IGrantSignInService"/> as scoped services, plus <see cref="SystemDateTimeProvider"/> when no
    /// <see cref="IDateTimeProvider"/> is registered.
    /// </summary>
    /// <remarks>
    /// The stores are the host's: register an <see cref="IResourceGrantStore"/> and an <see cref="IInvitationStore"/> on the
    /// backend of your choice. Resolving a service without them fails at that moment, naming the missing store.
    /// </remarks>
    public static BirkoGrantsBuilder AddBirkoGrants(this IServiceCollection services, Action<GrantOptions> configure,
        Action<GrantSignInOptions>? configureSignIn = null, Action<GrantsAspNetCoreOptions>? configureHttp = null)
    {
        if (services == null) throw new ArgumentNullException(nameof(services));
        if (configure == null) throw new ArgumentNullException(nameof(configure));

        var options = new GrantOptions();
        configure(options);
        services.AddSingleton(options.Build());

        var signIn = new GrantSignInOptions();
        configureSignIn?.Invoke(signIn);
        services.AddSingleton(signIn);

        var http = new GrantsAspNetCoreOptions();
        configureHttp?.Invoke(http);
        services.AddSingleton(http);

        services.TryAddSingleton<IDateTimeProvider, SystemDateTimeProvider>();
        if (!services.Any(d => d.ServiceType == typeof(IGrantAdminSource) && !d.IsKeyedService))
        {
            services.AddScoped<IGrantAdminSource>(sp => new BootstrapAdminGrantSource(sp.GetRequiredService<IResourceGrantStore>()));
            services.AddSingleton<DefaultAdminSourceMarker>();
        }

        services.AddScoped<GrantService>(sp => new GrantService(
            sp.GetRequiredService<IResourceGrantStore>(), sp.GetRequiredService<GrantRoleTable>(),
            sp.GetRequiredService<IDateTimeProvider>(), sp.GetRequiredService<IGrantAdminSource>()));
        services.AddScoped<IGrantService>(sp => sp.GetRequiredService<GrantService>());

        services.AddScoped<InvitationService>(sp => new InvitationService(
            sp.GetRequiredService<IInvitationStore>(), sp.GetRequiredService<IGrantService>(), sp.GetRequiredService<GrantRoleTable>(),
            sp.GetRequiredService<IDateTimeProvider>(), sp.GetRequiredService<GrantSignInOptions>()));
        services.AddScoped<IInvitationService>(sp => sp.GetRequiredService<InvitationService>());

        services.AddScoped<AdminGrantService>(sp => new AdminGrantService(
            sp.GetRequiredService<IResourceGrantStore>(), sp.GetRequiredService<IDateTimeProvider>(), sp.GetRequiredService<IGrantAdminSource>()));
        services.AddScoped<IAdminGrantService>(sp => sp.GetRequiredService<AdminGrantService>());

        services.AddScoped<GrantSignInService>(sp => new GrantSignInService(
            sp.GetRequiredService<IInvitationStore>(), sp.GetRequiredService<InvitationService>(), sp.GetRequiredService<AdminGrantService>(),
            sp.GetRequiredService<IDateTimeProvider>(), sp.GetRequiredService<GrantSignInOptions>()));
        services.AddScoped<IGrantSignInService>(sp => sp.GetRequiredService<GrantSignInService>());

        return new BirkoGrantsBuilder(services);
    }

    /// <summary>
    /// Decorates the host's registered <see cref="IUserPermissionResolver"/> with <see cref="GrantAwareUserPermissionResolver"/>
    /// (admin grant → <c>"*"</c>, type-wide grants → <c>"{type}:{action}"</c>), and makes the host's own <c>"*"</c> holders
    /// grant admins (<see cref="UserPermissionResolverAdminSource"/>). Call it <b>after</b> registering the resolver, and use
    /// <c>UseResolvedPermissions()</c> + <c>UseBirkoPermissionResolution()</c> (Birko.Security.AspNetCore) so the resolved
    /// permissions reach <c>ICurrentUser</c>.
    /// </summary>
    /// <remarks>
    /// The decorator keeps the host resolver's lifetime, so the <see cref="IResourceGrantStore"/> must live at least as
    /// long (a singleton store suits any resolver; a scoped one, only a scoped resolver — scope validation reports a
    /// mismatch). A resolver this call builds from a type or a factory is disposed with its scope, as the container would
    /// have disposed it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// No (non-keyed) <see cref="IUserPermissionResolver"/> is registered yet; this was already applied; or the host
    /// registered its own <see cref="IGrantAdminSource"/>, which this would otherwise replace without a word — compose
    /// <see cref="UserPermissionResolverAdminSource"/> into your source yourself instead.
    /// </exception>
    public static BirkoGrantsBuilder WithGrantPermissions(this BirkoGrantsBuilder builder)
    {
        if (builder == null) throw new ArgumentNullException(nameof(builder));
        var services = builder.Services;

        if (services.Any(d => d.ServiceType == typeof(HostPermissionResolver)))
            throw new InvalidOperationException("WithGrantPermissions was already applied.");
        var original = services.LastOrDefault(d => d.ServiceType == typeof(IUserPermissionResolver) && !d.IsKeyedService)
            ?? throw new InvalidOperationException(
                "WithGrantPermissions decorates the host's IUserPermissionResolver, and none is registered. Register yours first " +
                "(services.AddScoped<IUserPermissionResolver, MyResolver>()), then call WithGrantPermissions.");
        var adminSources = services.Where(d => d.ServiceType == typeof(IGrantAdminSource) && !d.IsKeyedService).ToList();
        if (adminSources.Count > 0 && !services.Any(d => d.ServiceType == typeof(DefaultAdminSourceMarker)))
            throw new InvalidOperationException(
                "An IGrantAdminSource of the host's own is registered; WithGrantPermissions would replace it. Compose " +
                "UserPermissionResolverAdminSource into your source instead, or register yours after WithGrantPermissions.");

        services.Remove(original);
        services.Add(new ServiceDescriptor(typeof(HostPermissionResolver), sp => HostPermissionResolver.Create(sp, original), original.Lifetime));
        services.Add(new ServiceDescriptor(typeof(IUserPermissionResolver), sp => new GrantAwareUserPermissionResolver(
            sp.GetRequiredService<HostPermissionResolver>().Resolver, sp.GetRequiredService<IResourceGrantStore>(), sp.GetRequiredService<GrantRoleTable>()),
            original.Lifetime));

        foreach (var source in adminSources)
            services.Remove(source);
        services.AddScoped<IGrantAdminSource>(sp => new UserPermissionResolverAdminSource(
            sp.GetRequiredService<HostPermissionResolver>().Resolver, sp.GetRequiredService<IResourceGrantStore>()));
        return builder;
    }

    /// <summary>Marks that the registered <see cref="IGrantAdminSource"/> is <see cref="AddBirkoGrants"/>' default, not the host's.</summary>
    internal sealed class DefaultAdminSourceMarker { }

    /// <summary>
    /// The host's undecorated resolver, kept under its own service type so the decorator and the admin source can reach it.
    /// Disposes the resolver when it built it, since the container no longer sees it as the registered service.
    /// </summary>
    internal sealed class HostPermissionResolver : IDisposable, IAsyncDisposable
    {
        private readonly bool _owned;

        private HostPermissionResolver(IUserPermissionResolver resolver, bool owned)
        {
            Resolver = resolver;
            _owned = owned;
        }

        public IUserPermissionResolver Resolver { get; }

        public static HostPermissionResolver Create(IServiceProvider sp, ServiceDescriptor original)
        {
            if (original.ImplementationInstance is IUserPermissionResolver instance)
                return new HostPermissionResolver(instance, owned: false);
            if (original.ImplementationFactory != null)
                return new HostPermissionResolver((IUserPermissionResolver)original.ImplementationFactory(sp), owned: true);
            if (original.ImplementationType != null)
                return new HostPermissionResolver((IUserPermissionResolver)ActivatorUtilities.CreateInstance(sp, original.ImplementationType), owned: true);
            throw new InvalidOperationException("The registered IUserPermissionResolver has no instance, factory or type to build it from.");
        }

        public void Dispose()
        {
            if (_owned && Resolver is IDisposable disposable)
                disposable.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_owned) return;
            if (Resolver is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (Resolver is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
