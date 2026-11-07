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
        /// <summary>The schema of <see cref="TableName"/>, or null for the connection's default (TASK-272).</summary>
        string? TableSchema => null;
        IReadOnlyList<FieldDescriptor> Properties { get; }
    }

    public class ModelMap<T> : IModelMap where T : class
    {
        public string? TableName { get; private set; }
        public string? TableSchema { get; private set; }
        public List<FieldDescriptor> Properties { get; } = new List<FieldDescriptor>();

        IReadOnlyList<FieldDescriptor> IModelMap.Properties => Properties;

        public ModelMap<T> ToTable(string tableName)
        {
            TableName = tableName;
            return this;
        }

        /// <summary>Maps to <paramref name="tableName"/> in <paramref name="schema"/> (TASK-272); see <c>[Table(Schema = …)]</c>.</summary>
        public ModelMap<T> ToTable(string tableName, string schema)
        {
            TableName = tableName;
            TableSchema = schema;
            return this;
        }

        public FieldBuilder<T> Property<TProp>(Expression<Func<T, TProp>> propertyExpression)
        {
            var memberName = GetMemberName(propertyExpression);
            // TASK-712: a second call for one member configures the same descriptor, as HasUnique and Ignore already did.
            // A fresh one each time let one index be declared twice past FieldBuilder's refusal.
            var field = Properties.Find(p => p.Name == memberName);
            if (field == null)
            {
                field = new FieldDescriptor(memberName);
                Properties.Add(field);
            }
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

        /// <summary>
        /// The property an entry point maps: <c>x =&gt; x.Name</c> (or <c>x =&gt; (object)x.Name</c>) is <c>"Name"</c>.
        /// TASK-845: a member that is not read directly off the parameter is refused by name. <c>x =&gt; x.Sub.X</c> used
        /// to be recorded as <c>"X"</c>, so the column name, flags, length and indexes landed on a top-level <c>X</c>
        /// (measured), and so did a string's <c>Length</c>, a Nullable's <c>Value</c> and a captured object's member.
        /// A SQL table has no nested member, so this is a one-segment rule, kept here rather than reusing
        /// <c>ViewDefinitionBuilder.GetMemberPath</c> (TASK-843): that producer resolves nested paths, which this caller
        /// must refuse, and it lives in <c>Birko.Data.Views</c>, which nothing on the SQL mapping side references.
        /// </summary>
        private static string GetMemberName<TProp>(Expression<Func<T, TProp>> expression)
        {
            var member = expression.Body as MemberExpression
                ?? (expression.Body as UnaryExpression)?.Operand as MemberExpression
                ?? throw new ArgumentException("Expression must be a member access expression.", nameof(expression));
            if (member.Expression is ParameterExpression)
            {
                return member.Member.Name;
            }

            var names = new List<string> { member.Member.Name };
            for (var owner = member.Expression as MemberExpression; owner != null; owner = owner.Expression as MemberExpression)
            {
                names.Insert(0, owner.Member.Name);
            }
            throw new NotSupportedException(
                $"'{expression}' maps '{string.Join(".", names)}', which is not a property of {typeof(T).Name}: a SQL table has "
                + $"no nested member. Map a property of {typeof(T).Name} itself. (TASK-845)");
        }
    }
}
