using Birko.Data.Tenant.Models;
using Birko.Serialization;
using Birko.Serialization.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
// System.Linq is required explicitly (CR-L230): FirstOrDefault over StringValues is a LINQ extension,
// and this shared .projitems source must compile in consumers that disable ImplicitUsings.
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Middleware;

/// <summary>
/// ASP.NET Core middleware for automatic tenant resolution from HTTP requests
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantContext? _tenantContext;
    private readonly TenantMiddlewareOptions _options;
    private readonly ISerializer _serializer;

    /// <summary>
    /// Create a new tenant middleware.
    /// </summary>
    /// <remarks>
    /// SH-H049: <paramref name="tenantContext"/> is <b>optional</b> and supplying it pins the middleware to
    /// <i>one</i> instance for the application's lifetime. Prefer leaving it null and letting
    /// <see cref="InvokeAsync(HttpContext, ITenantContext)"/> take the context per request, which is what
    /// <c>UseTenantMiddleware</c> now does — see that method's remarks for why. It is kept as a parameter
    /// so a caller constructing the middleware by hand (tests, a non-DI pipeline) can still supply one.
    /// </remarks>
    public TenantMiddleware(
        RequestDelegate next,
        ITenantContext? tenantContext = null,
        TenantMiddlewareOptions? options = null,
        ISerializer? serializer = null)
    {
        _serializer = serializer ?? new SystemJsonSerializer();
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _tenantContext = tenantContext;
        _options = options ?? new TenantMiddlewareOptions();
    }

    /// <summary>
    /// Process the HTTP request, taking the tenant context <b>per request</b>.
    /// </summary>
    /// <remarks>
    /// SH-H049. ASP.NET Core injects any extra <c>InvokeAsync</c> parameter from the <b>request</b> scope,
    /// which is the only way a singleton middleware can observe a scoped <see cref="ITenantContext"/>.
    /// Previously <c>UseTenantMiddleware</c> resolved the context once from
    /// <c>builder.ApplicationServices</c> — the <b>root</b> provider — and passed it as a constructor
    /// argument, so under the documented <c>AddTenantContextScoped()</c> the middleware set the tenant on a
    /// different instance from the one every request-scoped store read. Because
    /// <c>BelongsToCurrentTenant</c> deliberately fails open when <c>HasTenant == false</c> (CR-L229,
    /// pinned by <c>TenantFailOpenTests</c>), those stores then read and wrote across <i>every</i> tenant.
    /// In Development <c>ValidateScopes</c> turned the root resolution into a start-up throw; in Production
    /// it is silent, which is the configuration that mattered.
    /// <para>
    /// A constructor-supplied context still wins, so a hand-built pipeline keeps working; the injected one
    /// is used whenever none was supplied.
    /// </para>
    /// </remarks>
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // A hand-supplied context wins; otherwise use the one injected from THIS request's scope.
        var ambient = _tenantContext ?? tenantContext
            ?? throw new ArgumentNullException(nameof(tenantContext));

        // Try to resolve tenant from configured sources
        var resolved = ResolveTenantGuid(context);

        if (resolved.HasValue)
        {
            var (tenantGuid, source) = resolved.Value;

            // Set the tenant for this request
            var tenantName = ResolveTenantName(context, tenantGuid);
            ambient.SetTenant(tenantGuid, tenantName);

            // Add tenant to HTTP context for easy access
            context.Items[_options.TenantContextKey] = tenantGuid;

            // SH-H048: publish under the fixed key too, so the post-authentication tenant/claim guard can
            // correlate whatever door was used. TenantContextKey above is configurable and the context
            // may be a different instance from the one downstream middleware resolves — see ResolvedTenant.
            ResolvedTenant.Publish(context, tenantGuid, source);
        }
        else if (_options.RequireTenant)
        {
            // Tenant is required but not found - return 401
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            context.Response.ContentType = "application/json";

            var error = new
            {
                error = "Tenant Required",
                message = _options.TenantRequiredMessage ?? "A valid tenant identifier is required"
            };

            await context.Response.WriteAsync(_serializer.Serialize(error));
            return;
        }

        await _next(context);

        // Clear tenant after request completes
        ambient.ClearTenant();
    }

    /// <summary>
    /// Resolve tenant ID from the HTTP request, along with a description of the source that produced it.
    /// </summary>
    /// <remarks>
    /// The source description is carried so the tenant/claim guard can name the door in its 403 body
    /// (SH-H048). Keep these descriptions free of quotes and backslashes — see <see cref="ResolvedTenant"/>.
    /// </remarks>
    private (Guid TenantGuid, string Source)? ResolveTenantGuid(HttpContext context)
    {
        // 1. Check header
        if (!string.IsNullOrEmpty(_options.TenantHeaderName))
        {
            if (context.Request.Headers.TryGetValue(_options.TenantHeaderName, out var headerValue))
            {
                if (Guid.TryParse(headerValue.FirstOrDefault(), out var tenantGuid))
                {
                    return (tenantGuid, $"the {Describe(_options.TenantHeaderName)} header");
                }
            }
        }

        // 2. Check query string
        if (!string.IsNullOrEmpty(_options.TenantQueryStringKey))
        {
            if (context.Request.Query.TryGetValue(_options.TenantQueryStringKey, out var queryValue))
            {
                if (Guid.TryParse(queryValue.FirstOrDefault(), out var tenantGuid))
                {
                    return (tenantGuid, $"the {Describe(_options.TenantQueryStringKey)} query string parameter");
                }
            }
        }

        // 3. Check route values
        if (!string.IsNullOrEmpty(_options.TenantRouteKey))
        {
            if (context.GetRouteValue(_options.TenantRouteKey) is string routeValue)
            {
                if (Guid.TryParse(routeValue, out var tenantGuid))
                {
                    return (tenantGuid, $"the {Describe(_options.TenantRouteKey)} route value");
                }
            }
        }

        // 4. Custom resolver
        if (_options.CustomTenantResolver != null)
        {
            var custom = _options.CustomTenantResolver(context);
            if (custom.HasValue)
            {
                return (custom.Value, "the custom tenant resolver");
            }
        }

        return null;
    }

    /// <summary>
    /// Render a configured key for use in the guard's JSON 403 body: quotes and backslashes are stripped
    /// rather than escaped, because the key is consumer-configured and the body is hand-written JSON.
    /// </summary>
    private static string Describe(string key)
        => new string(key.Where(c => c != '"' && c != '\\' && !char.IsControl(c)).ToArray());

    /// <summary>
    /// Resolve tenant name from the HTTP request
    /// </summary>
    private string? ResolveTenantName(HttpContext context, Guid tenantGuid)
    {
        // Check header for tenant name
        if (!string.IsNullOrEmpty(_options.TenantNameHeaderName))
        {
            if (context.Request.Headers.TryGetValue(_options.TenantNameHeaderName, out var headerValue))
            {
                return headerValue.FirstOrDefault();
            }
        }

        // Custom name resolver
        if (_options.CustomTenantNameResolver != null)
        {
            return _options.CustomTenantNameResolver(context, tenantGuid);
        }

        return null;
    }
}

/// <summary>
/// Options for configuring tenant middleware
/// </summary>
public class TenantMiddlewareOptions
{
    /// <summary>
    /// Header name to read tenant ID from (default: "X-Tenant-Id")
    /// </summary>
    public string TenantHeaderName { get; set; } = "X-Tenant-Id";

    /// <summary>
    /// Header name to read tenant name from (default: "X-Tenant-Name")
    /// </summary>
    public string TenantNameHeaderName { get; set; } = "X-Tenant-Name";

    /// <summary>
    /// Query string key to read tenant ID from
    /// </summary>
    public string? TenantQueryStringKey { get; set; }

    /// <summary>
    /// Route parameter key to read tenant ID from
    /// </summary>
    public string? TenantRouteKey { get; set; }

    /// <summary>
    /// Whether a tenant is required (returns 401 if not found)
    /// </summary>
    public bool RequireTenant { get; set; } = false;

    /// <summary>
    /// Error message when tenant is required but not found
    /// </summary>
    public string? TenantRequiredMessage { get; set; }

    /// <summary>
    /// Key used to store tenant in HttpContext.Items
    /// </summary>
    public string TenantContextKey { get; set; } = "TenantId";

    /// <summary>
    /// Custom function to resolve tenant ID from request
    /// </summary>
    public Func<HttpContext, Guid?>? CustomTenantResolver { get; set; }

    /// <summary>
    /// Custom function to resolve tenant name from request
    /// </summary>
    public Func<HttpContext, Guid, string?>? CustomTenantNameResolver { get; set; }
}

/// <summary>
/// Extension methods for adding tenant middleware to the pipeline
/// </summary>
public static class TenantMiddlewareExtensions
{
    /// <summary>
    /// Add tenant middleware to the ASP.NET Core pipeline
    /// </summary>
    public static IApplicationBuilder UseTenantMiddleware(
        this IApplicationBuilder builder,
        Action<TenantMiddlewareOptions>? configureOptions = null)
    {
        var options = new TenantMiddlewareOptions();
        configureOptions?.Invoke(options);

        // SH-H049: do NOT resolve ITenantContext here. builder.ApplicationServices is the ROOT provider,
        // so a `AddTenantContextScoped()` registration yielded an instance no request-scoped store ever
        // reads — the middleware's SetTenant went to the wrong object and, because the tenant wrappers
        // deliberately fail open on HasTenant == false (CR-L229), every store then operated across all
        // tenants. The context is taken per request by InvokeAsync instead.
        //
        // The friendly start-up error is kept, using IServiceProviderIsService so the question "is it
        // registered?" is answered WITHOUT instantiating anything: resolving a scoped service from the
        // root provider is exactly the thing being removed, and under ValidateScopes it throws.
        // The presence check is guarded because IServiceProviderIsService is a default-container service:
        // a third-party DI container need not provide it, and in that case the check is skipped and the
        // per-request resolve below raises its own (clear) error instead. Skipping it is deliberate, not
        // an oversight — it only ever loses an earlier, friendlier message.
        var isService = builder.ApplicationServices.GetService<IServiceProviderIsService>();
        if (isService is not null && !isService.IsService(typeof(ITenantContext)))
        {
            throw new InvalidOperationException(
                $"{nameof(ITenantContext)} is not registered in the DI container. " +
                $"Call services.AddTenantContext() in ConfigureServices first."
            );
        }

        // ⚠ Deliberately NOT `UseMiddleware<TenantMiddleware>(...)`. That helper matches the supplied args
        // to constructor parameters through ActivatorUtilities, which cannot bind a `null` — so passing
        // null for the context would leave that parameter unmatched and let it be filled from the
        // application (ROOT) provider, silently reinstating the very capture SH-H040's sibling SH-H049 is
        // about. Resolving from `ctx.RequestServices` is unambiguous: that IS the request scope, so a
        // scoped ITenantContext registration is the instance every store in the same request observes.
        return builder.Use(next => async ctx =>
        {
            var perRequest = ctx.RequestServices.GetRequiredService<ITenantContext>();
            var middleware = new TenantMiddleware(next, tenantContext: null, options: options);
            await middleware.InvokeAsync(ctx, perRequest).ConfigureAwait(false);
        });
    }
}
