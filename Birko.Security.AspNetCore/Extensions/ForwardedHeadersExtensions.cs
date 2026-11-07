using System;
using System.Linq;
using Birko.Security.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Security.AspNetCore;

/// <summary>
/// One registration for "who is the client" behind a reverse proxy (TASK-829): ASP.NET's forwarded-headers middleware,
/// the loopback bypass check and the WebSocket token binding all read the one list registered here.
/// </summary>
public static class ForwardedHeadersExtensions
{
    /// <summary>
    /// Registers the forwarded headers from <paramref name="section"/> (normally
    /// <c>configuration.GetSection(BirkoForwardedHeadersOptions.SectionName)</c>). See
    /// <see cref="AddBirkoForwardedHeaders(IServiceCollection, Action{BirkoForwardedHeadersOptions})"/>.
    /// </summary>
    public static IServiceCollection AddBirkoForwardedHeaders(this IServiceCollection services, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return services.AddBirkoForwardedHeaders(options => section.Bind(options));
    }

    /// <summary>
    /// Believes <c>X-Forwarded-For</c> (and <c>-Proto</c>, and <c>-Host</c> against
    /// <see cref="BirkoForwardedHeadersOptions.AllowedHosts"/>) only from
    /// <see cref="TrustedProxySettings.TrustedProxies"/>.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Nothing configured trusts nobody. ASP.NET's own options trust loopback by default, and trust <b>every</b>
    /// sender once both of its lists are empty; both are replaced here, and with an empty list the headers are switched
    /// off, so a manual <c>UseForwardedHeaders()</c> or <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> trusts nobody either.</item>
    /// <item>A malformed entry, an unset <c>${VAR}</c>, a blank entry, <c>ForwardLimit</c> below 1, or allowed hosts
    /// with no proxy throw <see cref="InvalidOperationException"/> here, at startup.</item>
    /// <item>There is no <c>Use…</c> call: the middleware is put at the front of the pipeline by a startup filter, so it
    /// runs before authentication (including the one WebApplication adds on its own), the rate limiter, the loopback
    /// bypass and the tenant resolvers, wherever the app places those.</item>
    /// </list>
    /// </remarks>
    public static IServiceCollection AddBirkoForwardedHeaders(
        this IServiceCollection services,
        Action<BirkoForwardedHeadersOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(d => d.ServiceType == typeof(ForwardedHeadersRegistration)))
        {
            throw new InvalidOperationException(
                "AddBirkoForwardedHeaders was called twice. A host has one list of trusted proxies; configure it once.");
        }

        var options = new BirkoForwardedHeadersOptions();
        configure(options);
        var proxies = Validate(options);

        services.AddSingleton(options);
        // A copy, so a later change to the options object cannot make the checks read a list ASP.NET is not using.
        services.AddSingleton(new TrustedProxySettings { TrustedProxies = new(options.TrustedProxies) });
        services.AddSingleton(new ForwardedHeadersRegistration(proxies));
        // PostConfigure: after every Configure, including ASP.NET's own for ASPNETCORE_FORWARDEDHEADERS_ENABLED (which
        // empties both lists, i.e. trusts every sender), whatever order they were registered in.
        services.PostConfigure<ForwardedHeadersOptions>(o => Apply(o, proxies, options));
        // First, so it wraps every other startup filter and the app's whole pipeline.
        services.Insert(0, ServiceDescriptor.Singleton<IStartupFilter>(new ForwardedHeadersStartupFilter(proxies)));
        return services;
    }

    private static TrustedProxyList Validate(BirkoForwardedHeadersOptions options)
    {
        TrustedProxyList proxies;
        try
        {
            proxies = TrustedProxyList.Parse(options.TrustedProxies);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{BirkoForwardedHeadersOptions.SectionName}:TrustedProxies: {ex.Message}", ex);
        }
        if (options.ForwardLimit < 1)
        {
            throw new InvalidOperationException(
                $"{BirkoForwardedHeadersOptions.SectionName}:ForwardLimit is {options.ForwardLimit}; it must be at least 1.");
        }
        var hosts = options.AllowedHosts ??= new();
        if (hosts.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException($"{BirkoForwardedHeadersOptions.SectionName}:AllowedHosts has a blank entry.");
        }
        if (hosts.Count > 0 && proxies.IsEmpty)
        {
            throw new InvalidOperationException(
                $"{BirkoForwardedHeadersOptions.SectionName}:AllowedHosts is set, but TrustedProxies is empty, so no "
                + "X-Forwarded-Host would ever be believed. List the proxy, or remove the hosts.");
        }
        return proxies;
    }

    private static void Apply(ForwardedHeadersOptions target, TrustedProxyList proxies, BirkoForwardedHeadersOptions options)
    {
        target.KnownProxies.Clear();
        target.KnownIPNetworks.Clear();
        target.AllowedHosts.Clear();
        if (proxies.IsEmpty)
        {
            // Both lists empty means "trust every sender" to ASP.NET; with nothing to trust, read no header at all.
            target.ForwardedHeaders = ForwardedHeaders.None;
            return;
        }

        foreach (var address in proxies.Addresses) target.KnownProxies.Add(address);
        foreach (var network in proxies.Networks) target.KnownIPNetworks.Add(network);
        target.ForwardLimit = options.ForwardLimit;
        target.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
        if (options.ForwardProto) target.ForwardedHeaders |= ForwardedHeaders.XForwardedProto;
        if (options.AllowedHosts.Count > 0)
        {
            target.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;
            foreach (var host in options.AllowedHosts) target.AllowedHosts.Add(host.Trim());
        }
    }
}

/// <summary>The parsed list, so the startup checks need not parse it again.</summary>
internal sealed class ForwardedHeadersRegistration
{
    public ForwardedHeadersRegistration(TrustedProxyList proxies) => Proxies = proxies;

    public TrustedProxyList Proxies { get; }
}

/// <summary>
/// Puts ASP.NET's forwarded-headers middleware at the very front of the pipeline when any proxy is trusted, and marks
/// the request (<see cref="TrustedProxySettings.ResolvedItemKey"/>) so the WebSocket token binding takes the address
/// it produced instead of reading the headers a second time.
/// </summary>
internal sealed class ForwardedHeadersStartupFilter : IStartupFilter
{
    /// <summary>The headers ASP.NET reads; what it leaves unread is removed once it has run.</summary>
    private static readonly string[] Forwarded =
        { "X-Forwarded-For", "X-Forwarded-Proto", "X-Forwarded-Host", "X-Forwarded-Prefix" };

    private readonly TrustedProxyList _proxies;

    public ForwardedHeadersStartupFilter(TrustedProxyList proxies) => _proxies = proxies;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (!_proxies.IsEmpty)
        {
            app.UseForwardedHeaders();
            app.Use((context, nextMiddleware) =>
            {
                // A second ForwardedHeadersMiddleware (the app's own UseForwardedHeaders(), or the one
                // ASPNETCORE_FORWARDEDHEADERS_ENABLED adds) would read the values this pass left: past the forward
                // limit, a hop the client wrote. When the address it reached is itself trusted, that hop would be
                // believed. Nothing past the limit is trustworthy, so nothing is left to read; ASP.NET keeps what it
                // consumed in X-Original-*.
                foreach (var header in Forwarded) context.Request.Headers.Remove(header);
                context.Items[TrustedProxySettings.ResolvedItemKey] = true;
                return nextMiddleware(context);
            });
        }
        next(app);
    };
}
