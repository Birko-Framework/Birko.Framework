using Birko.Data.Expressions;
using Birko.Data.Patterns.Models;
using Birko.Data.Stores;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Shared filter logic for soft-delete decorators.
/// </summary>
public static class SoftDeleteFilter
{
    /// <summary>
    /// Combines a user filter with a DeletedAt == null condition.
    /// </summary>
    public static Expression<Func<T, bool>> CombineWithNotDeleted<T>(Expression<Func<T, bool>>? filter)
        where T : ISoftDeletable
    {
        Expression<Func<T, bool>> notDeleted = x => x.DeletedAt == null;
        return ExpressionParameterReplacer.AndAlso(filter, notDeleted);
    }

    /// <summary>
    /// Stamps <paramref name="item"/> deleted at <paramref name="now"/>; a default stops being one.
    /// </summary>
    /// <remarks>
    /// TASK-638: a deleted row is invisible to the Default decorator's probes, so a marker it kept under the unique index
    /// refused every later promotion, and no retry could help.
    /// </remarks>
    public static void Stamp<T>(T item, DateTime now) where T : ISoftDeletable
    {
        item.DeletedAt = now;
        if (item is Birko.Data.Models.IDefault defaultable) defaultable.IsDefault = false;
        if (item is IDefaultMarker marked) marked.DefaultMarker = null;
    }

    /// <summary>The native update of a filtered soft delete: <see cref="Stamp"/> as one statement.</summary>
    public static PropertyUpdate<T> Deletion<T>(DateTime now) where T : Birko.Data.Models.AbstractModel, ISoftDeletable
    {
        var deletion = new PropertyUpdate<T>().Set(x => x.DeletedAt, (DateTime?)now);
        if (typeof(Birko.Data.Models.IDefault).IsAssignableFrom(typeof(T)))
            deletion.Set(Property<T, bool>(nameof(Birko.Data.Models.IDefault.IsDefault)), false);
        if (typeof(IDefaultMarker).IsAssignableFrom(typeof(T)))
            deletion.Set(Property<T, bool?>(nameof(IDefaultMarker.DefaultMarker)), null);
        return deletion;
    }

    private static Expression<Func<T, TProperty>> Property<T, TProperty>(string name)
    {
        var x = Expression.Parameter(typeof(T), "x");
        return Expression.Lambda<Func<T, TProperty>>(Expression.Property(x, name), x);
    }
}

/// <summary>
/// A soft-delete decorator. Its inner store also sees deleted rows, which is where the Sluggable probe asks, because a
/// deleted row keeps its slug (TASK-638).
/// </summary>
public interface ISoftDeleteStoreWrapper : IStoreWrapper
{
}
