using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.Patterns.Schema;

namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// CR-L322: non-generic view over a configured <see cref="ModelMap{T}"/> so the registry can read
    /// TableName/Properties by a typed cast instead of string-keyed reflection.
    /// </summary>
    public interface IModelMap
    {
        string? TableName { get; }
        IReadOnlyList<FieldDescriptor> Properties { get; }
    }

    public class ModelMap<T> : IModelMap where T : class
    {
        public string? TableName { get; private set; }
        public List<FieldDescriptor> Properties { get; } = new List<FieldDescriptor>();

        IReadOnlyList<FieldDescriptor> IModelMap.Properties => Properties;

        public ModelMap<T> ToTable(string tableName)
        {
            TableName = tableName;
            return this;
        }

        public FieldBuilder<T> Property<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var field = new FieldDescriptor(memberName);
            Properties.Add(field);
            return new FieldBuilder<T>(this, field);
        }

        public ModelMap<T> HasUnique<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.Name == memberName);
            if (existing != null)
            {
                existing.IsUnique = true;
            }
            else
            {
                var field = new FieldDescriptor(memberName) { IsUnique = true };
                Properties.Add(field);
            }
            return this;
        }

        public ModelMap<T> HasPrimary<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.Name == memberName);
            if (existing != null)
            {
                existing.IsPrimary = true;
            }
            else
            {
                var field = new FieldDescriptor(memberName) { IsPrimary = true };
                Properties.Add(field);
            }
            return this;
        }

        public ModelMap<T> Ignore<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            var existing = Properties.Find(p => p.Name == memberName);
            if (existing != null)
            {
                existing.IsIgnored = true;
            }
            else
            {
                var field = new FieldDescriptor(memberName) { IsIgnored = true };
                Properties.Add(field);
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
