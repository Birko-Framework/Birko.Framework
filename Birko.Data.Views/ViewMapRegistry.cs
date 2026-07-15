using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Birko.Data.Views;

/// <summary>
/// Central registry for view definitions. Discovers and caches IViewMapping implementations.
/// Follows the ModelMapRegistry pattern from Birko.Models.SQL.
/// </summary>
public class ViewMapRegistry
{
    private readonly Dictionary<Type, ViewDefinition> _definitions = new();

    /// <summary>Register a single view mapping.</summary>
    public void Register<TView>(IViewMapping<TView> mapping) where TView : class
    {
        var builder = new ViewDefinitionBuilder<TView>();
        mapping.Configure(builder);
        _definitions[typeof(TView)] = builder.Build();
    }

    /// <summary>Scan an assembly for all IViewMapping implementations and register them.</summary>
    public void RegisterFromAssembly(Assembly assembly)
    {
        // CR-L240: Assembly.GetTypes() throws ReflectionTypeLoadException if any type in the assembly
        // fails to load (e.g. a missing optional dependency). Fall back to the types that DID load so a
        // single unloadable type doesn't hard-fail startup discovery.
        var mappingTypes = GetLoadableTypes(assembly.GetTypes)
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IViewMapping<>))
                .Select(i => new { MappingType = t, ViewType = i.GetGenericArguments()[0], Interface = i }));

        foreach (var entry in mappingTypes)
        {
            var instance = Activator.CreateInstance(entry.MappingType);
            if (instance == null)
            {
                continue;
            }

            var builderType = typeof(ViewDefinitionBuilder<>).MakeGenericType(entry.ViewType);
            var builder = Activator.CreateInstance(builderType);
            if (builder == null)
            {
                continue;
            }

            var configureMethod = entry.Interface.GetMethod("Configure");
            // CR-L242: builder is provably non-null here (checked above); use object[] so the element
            // type is explicit object, not the inferred object?[] (keeps nullable-strict consumers clean).
            configureMethod?.Invoke(instance, new object[] { builder });

            var buildMethod = builderType.GetMethod("Build");
            if (buildMethod?.Invoke(builder, null) is ViewDefinition definition)
            {
                _definitions[entry.ViewType] = definition;
            }
        }
    }

    /// <summary>Get the view definition for a view type. Returns null if not registered.</summary>
    public ViewDefinition? GetDefinition<TView>() where TView : class
    {
        return _definitions.GetValueOrDefault(typeof(TView));
    }

    /// <summary>Get the view definition for a view type. Returns null if not registered.</summary>
    public ViewDefinition? GetDefinition(Type viewType)
    {
        return _definitions.GetValueOrDefault(viewType);
    }

    /// <summary>Check if a view definition is registered.</summary>
    public bool HasDefinition<TView>() where TView : class
    {
        return _definitions.ContainsKey(typeof(TView));
    }

    /// <summary>Check if a view definition is registered.</summary>
    public bool HasDefinition(Type viewType)
    {
        return _definitions.ContainsKey(viewType);
    }

    /// <summary>Get all registered view definitions.</summary>
    public IEnumerable<KeyValuePair<Type, ViewDefinition>> GetAll()
    {
        return _definitions;
    }

    /// <summary>
    /// Invokes <paramref name="getTypes"/> (typically <see cref="Assembly.GetTypes"/>) and, on a
    /// <see cref="ReflectionTypeLoadException"/>, falls back to the types that loaded successfully.
    /// Exposed as an injectable seam so the fallback can be tested without a purpose-built broken
    /// assembly (CR-L240 / CR-L243).
    /// </summary>
    internal static IEnumerable<Type> GetLoadableTypes(Func<Type[]> getTypes)
    {
        try
        {
            return getTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(t => t!);
        }
    }
}
