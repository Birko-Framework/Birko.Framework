using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Shared helper for applying <see cref="OrderBy{T}"/> sorting to collections and queryables.
    /// Eliminates duplication of dynamic ordering logic across store and view implementations.
    /// </summary>
    public static class OrderByHelper
    {
        /// <summary>
        /// Applies ordering to an <see cref="IQueryable{T}"/> using reflection-based expression building.
        /// Suitable for LINQ providers (RavenDB, Cosmos DB, EF Core, etc.).
        /// </summary>
        public static IQueryable<T> ApplyTo<T>(IQueryable<T> query, OrderBy<T>? orderBy)
        {
            if (orderBy?.Fields == null || orderBy.Fields.Count == 0)
            {
                return query;
            }

            for (int i = 0; i < orderBy.Fields.Count; i++)
            {
                var field = orderBy.Fields[i];
                var param = Expression.Parameter(typeof(T), "x");
                var property = Expression.Property(param, field.PropertyName);
                var lambda = Expression.Lambda(property, param);

                var methodName = i == 0
                    ? (field.Descending ? "OrderByDescending" : "OrderBy")
                    : (field.Descending ? "ThenByDescending" : "ThenBy");

                var method = typeof(Queryable).GetMethods()
                    .First(m => m.Name == methodName && m.GetParameters().Length == 2)
                    .MakeGenericMethod(typeof(T), property.Type);

                query = (IQueryable<T>)method.Invoke(null, new object[] { query, lambda })!;
            }

            return query;
        }

        /// <summary>
        /// Applies ordering to an <see cref="IEnumerable{T}"/> using compiled expression delegates.
        /// Suitable for in-memory collections (JSON, XML, etc.).
        /// </summary>
        public static IEnumerable<T> ApplyTo<T>(IEnumerable<T> source, OrderBy<T>? orderBy)
        {
            if (orderBy?.Fields == null || orderBy.Fields.Count == 0)
            {
                return source;
            }

            IOrderedEnumerable<T>? orderedSource = null;

            foreach (var field in orderBy.Fields)
            {
                var param = Expression.Parameter(typeof(T), "x");
                var property = Expression.Property(param, field.PropertyName);
                var lambda = Expression.Lambda<Func<T, object>>(Expression.Convert(property, typeof(object)), param);
                var compiledFunc = lambda.Compile();

                if (orderedSource == null)
                {
                    orderedSource = field.Descending
                        ? source.OrderByDescending(compiledFunc)
                        : source.OrderBy(compiledFunc);
                }
                else
                {
                    orderedSource = field.Descending
                        ? orderedSource.ThenByDescending(compiledFunc)
                        : orderedSource.ThenBy(compiledFunc);
                }
            }

            return orderedSource ?? source;
        }
    }
}
