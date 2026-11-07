using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.SQL.Conditions
{
    public enum ConditionType
    {
        Equal,
        Less,
        Greather,
        LessAndEqual,
        GreatherAndEqual,
        IsNull,
        Like,
        StartsWith,
        EndsWith,
        In,
    }

    /// <summary>TASK-859: a comparison operand that can be NULL, as its <c>IS NULL</c> test renders it.</summary>
    public sealed class NullableOperand
    {
        public NullableOperand(string sql, Func<SqlValueFragmentDialect, string>? fragment = null)
        {
            Sql = sql;
            Fragment = fragment;
        }

        /// <summary>The operand in portable SQL: a column, or a value fragment.</summary>
        public string Sql { get; }

        /// <summary>The re-render of a value fragment for a provider dialect (TASK-856); null for a column.</summary>
        public Func<SqlValueFragmentDialect, string>? Fragment { get; }

        /// <summary>The operand's <c>IS NULL</c> (or, with <paramref name="isNot"/>, <c>IS NOT NULL</c>) leaf.</summary>
        public Condition IsNull(bool isNot) => new(Sql, null, ConditionType.IsNull, isNot: isNot) { NameFragment = Fragment };
    }

    public class Condition
    {
        public string? Name { get; set; }
        public IEnumerable? Values { get; set; }
        public bool IsField { get; set; } = false;
        public ConditionType Type { get; set; } = ConditionType.Equal;
        public IEnumerable<Condition>? SubConditions { get; set; } = null;
        public bool IsOr { get; set; } = false;
        public bool IsNot { get; set; } = false;

        /// <summary>
        /// TASK-856: re-renders <see cref="Name"/> (a value fragment) for a provider whose
        /// <see cref="SqlValueFragmentDialect"/> is not the portable one. Null for a column name.
        /// </summary>
        public Func<SqlValueFragmentDialect, string>? NameFragment { get; set; }

        /// <summary>TASK-856: the same for the single value of an <see cref="IsField"/> condition.</summary>
        public Func<SqlValueFragmentDialect, string>? FieldFragment { get; set; }

        /// <summary>
        /// TASK-859: the operands of this comparison that can be NULL, set by the expression parser. SQL answers UNKNOWN
        /// where C# answers true or false, so the parser adds an <c>IS [NOT] NULL</c> leg where the two would differ.
        /// Null for a condition built by hand, which keeps SQL's semantics.
        /// </summary>
        public IReadOnlyList<NullableOperand>? NullableOperands { get; set; }

        /// <summary>
        /// TASK-859: what the comparison answers in C#, before <see cref="IsNot"/>, when one of
        /// <see cref="NullableOperands"/> is NULL: false for <c>==</c> and the orderings, true for an <c>IN</c> whose list
        /// held <c>null</c>.
        /// </summary>
        public bool NullOperandMatches { get; set; }

        /// <summary>A copy carrying every property but <paramref name="values"/> and <paramref name="subConditions"/>.</summary>
        public Condition Copy(IEnumerable? values, IEnumerable<Condition>? subConditions)
            => new(Name, values, Type, IsField, IsNot, IsOr, subConditions)
            {
                NameFragment = NameFragment,
                FieldFragment = FieldFragment,
                NullableOperands = NullableOperands,
                NullOperandMatches = NullOperandMatches,
            };

        public Condition(string? name, IEnumerable? values, ConditionType type = ConditionType.Equal, bool isField = false, bool isNot = false, bool isOr = false, IEnumerable<Condition>? subConditions = null)
        {
            Name = name;
            Type = type;
            Values = values;
            IsField = isField;
            IsOr = isOr;
            IsNot = isNot;
            SubConditions = subConditions;
        }

        public static Condition Create(string name, IEnumerable values, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, values, type, false, isNot);
        }

        public static Condition CreateValue(string name, object value, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, new[] { value }, type, false, isNot);
        }

        public static Condition And(string name, IEnumerable values, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return Create(name, values, type, isNot);
        }

        public static Condition AndValue(string name, object value, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return CreateValue(name, value, type, isNot);
        }

        public static Condition AndField(string name, string field, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, new[] { field }, type, true, isNot);
        }

        public static Condition AndSubCondition(IEnumerable<Condition>? subConditions = null, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(null, null, type, false, isNot, false, subConditions);
        }

        public static Condition Or<T>(string name, IEnumerable<object> values, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, values, type, false, isNot, true);
        }

        public static Condition OrValue<T>(string name, object value, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, new[] { value }, type, false, isNot, true);
        }

        public static Condition OrField(string name, string field, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(name, new[] { field }, type, true, isNot, true);
        }

        public static Condition OrSubCondition(IEnumerable<Condition>? subConditions = null, ConditionType type = ConditionType.Equal, bool isNot = false)
        {
            return new Condition(null, null, type, false, isNot, true, subConditions);
        }
    }
}
