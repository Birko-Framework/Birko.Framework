using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Birko.Telemetry;

/// <summary>
/// ASP.NET Core middleware that reads or generates a correlation ID,
/// sets it as Activity baggage, and echoes it in the response header.
/// </summary>
public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly BirkoTelemetryOptions _options;

    public CorrelationIdMiddleware(RequestDelegate next, IOptions<BirkoTelemetryOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.EnableCorrelationId)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var headerName = _options.CorrelationIdHeaderName;

        var correlationId = context.Request.Headers.ContainsKey(headerName)
            ? context.Request.Headers[headerName].ToString()
            : null;

        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        Activity.Current?.SetBaggage("correlation-id", correlationId);

        context.Items["CorrelationId"] = correlationId;

        // CR-L380: the header is written here — before awaiting the rest of the pipeline — so it is always
        // present before any downstream component can start the response. Writing it eagerly at this point
        // cannot hit the "response already started" exception that context.Response.OnStarting guards
        // against (that risk only applies to middleware that mutates headers AFTER awaiting _next), so the
        // simpler eager write is correct here and also survives a downstream throw.
        context.Response.Headers[headerName] = correlationId;

        await _next(context).ConfigureAwait(false);
    }
}
