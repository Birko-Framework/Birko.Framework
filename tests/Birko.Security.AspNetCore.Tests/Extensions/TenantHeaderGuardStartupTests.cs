using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Birko.Security.AspNetCore.Tests.Extensions;

/// <summary>
/// SH-M354 (TASK-161). <c>RequireTenantHeaderMatchesClaim</c> defaults to true, but only the guard middleware reads it
/// and nothing added that middleware, so an app that never called <c>UseBirkoTenantHeaderGuard()</c> compared nothing.
/// Measured against a real host start (minimal hosting, ephemeral port), because whether a marker set on the app
/// reaches the startup filter's builder is a property of the hosting model, not something to assume.
/// </summary>
public class TenantHeaderGuardStartupTests
{
    private static WebApplication App(bool guard, bool require)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddBirkoSecurity(o =>
        {
            o.Jwt.Secret = "a-test-secret-that-is-long-enough-for-hmac-sha256!!";
            o.RequireTenantHeaderMatchesClaim = require;
        });
        var app = builder.Build();
        app.UseAuthentication();
        if (guard) app.UseBirkoTenantHeaderGuard();
        return app;
    }

    [Fact]
    public async Task A_host_without_the_guard_it_requires_refuses_to_start()
    {
        await using var app = App(guard: false, require: true);
        var start = () => app.StartAsync();
        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*UseBirkoTenantHeaderGuard*");
    }

    [Fact]
    public async Task A_host_with_the_guard_starts()
    {
        await using var app = App(guard: true, require: true);
        await app.StartAsync();
        await app.StopAsync();
    }

    [Fact]
    public async Task A_host_that_opted_out_starts_without_the_guard()
    {
        await using var app = App(guard: false, require: false);
        await app.StartAsync();
        await app.StopAsync();
    }
}
