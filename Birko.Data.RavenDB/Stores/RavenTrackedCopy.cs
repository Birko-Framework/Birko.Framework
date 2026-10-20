using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace Birko.Data.RavenDB.Stores;

/// <summary>
/// Copies a caller's entity onto the one a RavenDB session loaded and tracks (TASK-702), which is how the stores update
/// a document.
/// </summary>
/// <remarks>
/// Evicting the loaded document and storing the caller's object under its id loses what the load brought, among it the
/// atomic guard index of a document a cluster transaction wrote: inside a cluster-wide session (<c>RavenDbUnitOfWork</c>)
/// the save then failed with <c>ClusterTransactionConcurrencyException</c> (measured on 7.2). The tracked document keeps
/// it. What is copied is what RavenDB stores of an entity: public fields, and public properties with a setter of any
/// visibility, so a value the serializer writes is never left behind.
/// </remarks>
internal static class RavenTrackedCopy
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo[] Properties, FieldInfo[] Fields)> Members = new();

    /// <summary>Copies <paramref name="source"/>'s stored members onto <paramref name="tracked"/>.</summary>
    public static void Into<T>(T source, T tracked) where T : class
    {
        if (ReferenceEquals(source, tracked))
        {
            return;
        }
        // The runtime type: a derived entity stored through a base-typed store keeps its own members.
        var type = source.GetType();
        if (type != tracked.GetType())
        {
            throw new InvalidOperationException(
                $"The document loaded for the update is a {tracked.GetType().Name}, the entity given is a {type.Name}.");
        }
        var (properties, fields) = Members.GetOrAdd(type, Discover);
        foreach (var property in properties)
        {
            property.SetValue(tracked, property.GetValue(source));
        }
        foreach (var field in fields)
        {
            field.SetValue(tracked, field.GetValue(source));
        }
    }

    private static (PropertyInfo[], FieldInfo[]) Discover(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetGetMethod() != null && p.GetSetMethod(nonPublic: true) != null)
            .ToArray();
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance).Where(f => !f.IsInitOnly).ToArray();
        return (properties, fields);
    }
}
