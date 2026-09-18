using Microsoft.Extensions.DependencyInjection;

namespace Birko.Data.Tagging;

public static class TaggingExtensions
{
    /// <summary>
    /// Registers the ITagService in the DI container.
    /// Call this after registering repositories for Tag and EntityTag.
    /// </summary>
    public static IServiceCollection AddTagService<TImpl>(this IServiceCollection services)
        where TImpl : class, ITagService
    {
        services.AddScoped<ITagService, TImpl>();
        return services;
    }
}
