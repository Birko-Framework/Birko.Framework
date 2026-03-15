using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Represents an ordered list of sort fields for store queries.
    /// Provides expression-based API for compile-time safety.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    public class OrderBy<T>
    {
        private readonly List<OrderByField> _fields = new();

        /// <summary>
        /// Gets the ordered list of sort fields.
        /// </summary>
        public IReadOnlyList<OrderByField> Fields => _fields.AsReadOnly();

        private OrderBy() { }

        /// <summary>
        /// Creates an ascending sort on the specified property.
        /// </summary>
        public static OrderBy<T> By<TProperty>(Expression<Func<T, TProperty>> property)
        {
            var orderBy = new OrderBy<T>();
            orderBy._fields.Add(new OrderByField(GetPropertyName(property), false));
            return orderBy;
        }

        /// <summary>
        /// Creates a descending sort on the specified property.
        /// </summary>
        public static OrderBy<T> ByDescending<TProperty>(Expression<Func<T, TProperty>> property)
        {
            var orderBy = new OrderBy<T>();
            orderBy._fields.Add(new OrderByField(GetPropertyName(property), true));
            return orderBy;
        }

        /// <summary>
        /// Creates a sort from a property name string and direction.
        /// Use expression-based overloads when possible for compile-time safety.
        /// </summary>
        public static OrderBy<T> ByName(string propertyName, bool descending = false)
        {
            var orderBy = new OrderBy<T>();
            orderBy._fields.Add(new OrderByField(propertyName, descending));
            return orderBy;
        }

        /// <summary>
        /// Adds an ascending secondary sort.
        /// </summary>
        public OrderBy<T> ThenBy<TProperty>(Expression<Func<T, TProperty>> property)
        {
            _fields.Add(new OrderByField(GetPropertyName(property), false));
            return this;
        }

        /// <summary>
        /// Adds a descending secondary sort.
        /// </summary>
        public OrderBy<T> ThenByDescending<TProperty>(Expression<Func<T, TProperty>> property)
        {
            _fields.Add(new OrderByField(GetPropertyName(property), true));
            return this;
        }

        /// <summary>
        /// Converts to IDictionary for connector-level consumption.
        /// Key = property name, Value = true for descending.
        /// </summary>
        public IDictionary<string, bool> ToDictionary()
        {
            var dict = new Dictionary<string, bool>();
            foreach (var field in _fields)
            {
                dict[field.PropertyName] = field.Descending;
            }
            return dict;
        }

        private static string GetPropertyName<TProperty>(Expression<Func<T, TProperty>> expression)
        {
            if (expression.Body is MemberExpression member)
            {
                return member.Member.Name;
            }

            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                return unaryMember.Member.Name;
            }

            throw new ArgumentException("Expression must be a property access expression.", nameof(expression));
        }
    }

    /// <summary>
    /// Represents a single sort field with direction.
    /// </summary>
    public sealed record OrderByField(string PropertyName, bool Descending);
}
