using Birko.Data.Tenant.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace Birko.Data.Tenant.Middleware;

/// <summary>
/// ASP.NET Core middleware for automatic tenant resolution from HTTP requests
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantContext _tenantContext;
    private readonly TenantMiddlewareOptions _options;

    /// <summary>
    /// Create a new tenant middleware
    /// </summary>
    public TenantMiddleware(
        RequestDelegate next,
        ITenantContext tenantContext,
        TenantMiddlewareOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _options = options ?? new TenantMiddlewareOptions();
    }

    /// <summary>
    /// Process the HTTP request
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Try to resolve tenant from configured sources
        var tenantGuid = ResolveTenantGuid(context);

        if (tenantGuid.HasValue)
        {
            // Set the tenant for this request
            var tenantName = ResolveTenantName(context, tenantGuid.Value);
            _tenantContext.SetTenant(tenantGuid.Value, tenantName);

            // Add tenant to HTTP context for easy access
            context.Items[_options.TenantContextKey] = tenantGuid.Value;
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

            await context.Response.WriteAsync(JsonSerializer.Serialize(error));
            return;
        }

        await _next(context);

        // Clear tenant after request completes
        _tenantContext.ClearTenant();
    }

    /// <summary>
    /// Resolve tenant ID from the HTTP request
    /// </summary>
    private Guid? ResolveTenantGuid(HttpContext context)
    {
        // 1. Check header
        if (!string.IsNullOrEmpty(_options.TenantHeaderName))
        {
            if (context.Request.Headers.TryGetValue(_options.TenantHeaderName, out var headerValue))
            {
                if (Guid.TryParse(headerValue.FirstOrDefault(), out var tenantGuid))
                {
                    return tenantGuid;
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
                    return tenantGuid;
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
                    return tenantGuid;
                }
            }
        }

        // 4. Custom resolver
        if (_options.CustomTenantResolver != null)
        {
            return _options.CustomTenantResolver(context);
        }

        return null;
    }

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

        // Get ITenantContext from service provider
        var tenantContext = builder.ApplicationServices.GetService<ITenantContext>();
        if (tenantContext == null)
        {
            throw new InvalidOperationException(
                $"{nameof(ITenantContext)} is not registered in the DI container. " +
                $"Call services.AddTenantContext() in ConfigureServices first."
            );
        }

        return builder.UseMiddleware<TenantMiddleware>(tenantContext, options);
    }
}
