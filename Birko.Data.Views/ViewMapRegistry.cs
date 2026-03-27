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
        var mappingTypes = assembly.GetTypes()
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
            configureMethod?.Invoke(instance, new[] { builder });

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
}
