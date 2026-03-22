using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// Fluent SQL mapping configuration for a model type.
    /// Replaces attribute-based mapping ([Table], [UniqueField], [PrecisionField], etc.).
    /// </summary>
    public class ModelMap<T> where T : class
    {
        public string? TableName { get; private set; }
        public List<PropertyMap> Properties { get; } = new List<PropertyMap>();

        public ModelMap<T> ToTable(string tableName)
        {
            TableName = tableName;
            return this;
        }

        public PropertyMapBuilder<T> Property<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var map = new PropertyMap(memberName);
            Properties.Add(map);
            return new PropertyMapBuilder<T>(this, map);
        }

        public ModelMap<T> HasUnique<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.PropertyName == memberName);
            if (existing != null)
            {
                existing.IsUnique = true;
            }
            else
            {
                var map = new PropertyMap(memberName) { IsUnique = true };
                Properties.Add(map);
            }
            return this;
        }

        public ModelMap<T> HasPrimary<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.PropertyName == memberName);
            if (existing != null)
            {
                existing.IsPrimary = true;
            }
            else
            {
                var map = new PropertyMap(memberName) { IsPrimary = true };
                Properties.Add(map);
            }
            return this;
        }

        public ModelMap<T> Ignore<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.PropertyName == memberName);
            if (existing != null)
            {
                existing.IsIgnored = true;
            }
            else
            {
                var map = new PropertyMap(memberName) { IsIgnored = true };
                Properties.Add(map);
            }
            return this;
        }

        private static string GetMemberName<TProp>(Expression<Func<T, TProp>> expression)
        {
            if (expression.Body is MemberExpression member)
            {
                return member.Member.Name;
            }
            if (expression.Body is UnaryExpression unary && unary.Operand is MemberExpression unaryMember)
            {
                return unaryMember.Member.Name;
            }
            throw new ArgumentException("Expression must be a member access expression.", nameof(expression));
        }
    }
}
