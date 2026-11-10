using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;
using Birko.Data.SQL.Conditions;
using Birko.Data.Stores;

namespace Birko.Data.SQL.Fields
{
    public class FunctionField : AbstractField
    {
        public object[] Parameters { get; set; }

        /// <summary>
        /// The source column's own reader, writing into the view property: set for a MIN / MAX whose view property is not
        /// numeric, because that aggregate answers the source column's type and the column knows how its provider stores
        /// it. Null for every numeric aggregate, which <see cref="ToViewValue"/> converts.
        /// </summary>
        internal AbstractField? ValueReader { get; private set; }

        public FunctionField(System.Reflection.PropertyInfo property, string name, object[] parameters, DbType type = DbType.String, bool notNull = false)
            : base(property, name, type, false, notNull, false, false)
        {
            Parameters = parameters;
        }

        /// <summary>
        /// Reads an aggregate into its view property. Sealed, so every aggregate is read by this one rule (TASK-887).
        /// <para>
        /// The reader used to be picked by the aggregate's SQL function and its source column's field class: COUNT read an
        /// Int32, AVG a Decimal, SUM / MIN / MAX the source class's getter, and a source class missing from that list
        /// (long, short, double, float) was read as a string. So a <c>long</c> SUM, any <c>double</c> aggregate and an AVG
        /// into anything but a decimal threw on every provider (measured: InvalidCastException on PostgreSQL, MySQL and
        /// SQL Server, ArgumentException on SQLite). The type a server answers is not the source column's type either: a
        /// SUM over a BIGINT is NUMERIC on PostgreSQL and DECIMAL on MySQL, a SUM over a SMALLINT is BIGINT, INT or DECIMAL
        /// depending on the provider, and SQLite keeps a decimal column as text.
        /// </para>
        /// <para>
        /// A numeric view property now receives the server's value converted exactly to its type; a value it cannot hold
        /// exactly (a fraction in an integral property, NULL in a non-nullable one) is refused with
        /// <see cref="InvalidCastException"/>, one out of range with <see cref="OverflowException"/>. Any other view
        /// property is a MIN / MAX, read by <see cref="ValueReader"/>.
        /// </para>
        /// </summary>
        public sealed override void Read(object value, DbDataReader reader, int index)
        {
            if (ValueReader != null)
            {
                ValueReader.Read(value, reader, index);
                return;
            }
            Property.SetValue(value, ToViewValue(reader.IsDBNull(index) ? null : reader.GetValue(index)), null);
        }

        /// <summary>The aggregate as it is rendered, without a table qualifier, for messages.</summary>
        private string Describe() => string.Format("{0}({1})", Name, string.Join(",", Parameters));

        private string DescribeProperty() => $"{Property.DeclaringType?.Name}.{Property.Name}";

        /// <summary>
        /// Converts a numeric aggregate the server answered to the view property's type, exactly, or refuses it: the one
        /// reading rule, <see cref="AggregateValue.Convert"/>, which the store's <c>Aggregate</c> reads by too (TASK-892).
        /// </summary>
        internal object? ToViewValue(object? raw)
            => AggregateValue.Convert(raw, Property.PropertyType, Describe(), $"view property '{DescribeProperty()}'");

        /// <summary>
        /// The CLR type of the aggregated column's values, which decides how the provider renders the aggregate (an integral
        /// AVG as a double, SQLite's decimal SUM / AVG exactly). Null for a field built without a source column.
        /// </summary>
        internal Type? SourceType { get; private set; }

        /// <summary>
        /// TASK-892: the aggregate rendered by the provider's dialect, <see cref="SqlValueFragmentDialect.Aggregate"/>, the
        /// producer the store's <c>Aggregate</c> renders by too. A view's SUM over no values (a group of only NULLs) answers
        /// 0, as LINQ's <c>Sum</c> answers for <c>int?</c> as for <c>int</c>; it answered NULL on all four providers.
        /// </summary>
        protected internal override string AggregateExpression(string prefix, SqlValueFragmentDialect dialect)
        {
            var function = AggregateFunctionOf(Name);
            if (function == null || Parameters == null || Parameters.Length != 1 || (SourceType == null && function != AggregateFunction.Count))
            {
                return base.AggregateExpression(prefix, dialect);
            }
            try
            {
                return dialect.Aggregate(function.Value, prefix + Parameters[0], SourceType, Property.PropertyType);
            }
            catch (NotSupportedException ex)
            {
                throw new NotSupportedException($"View property '{DescribeProperty()}': {ex.Message}", ex);
            }
        }

        private static AggregateFunction? AggregateFunctionOf(string? name) => name switch
        {
            "COUNT" => AggregateFunction.Count,
            "SUM" => AggregateFunction.Sum,
            "AVG" => AggregateFunction.Avg,
            "MIN" => AggregateFunction.Min,
            "MAX" => AggregateFunction.Max,
            _ => null,
        };

        private static Type? ClrTypeOf(AbstractField field) => field.Property?.PropertyType ?? field switch
        {
            ShortField => typeof(short),
            LongField => typeof(long),
            IntegerField => typeof(int),
            DecimalField => typeof(decimal),
            DoubleField => typeof(double),
            FloatField => typeof(float),
            _ => null,
        };

        private static bool IsNumericTarget(PropertyInfo property)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return AggregateValue.IsNumeric(type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }

        private static bool IsIntegralSource(AbstractField field) => field is IntegerField or LongField or ShortField;

        private static bool IsNumericSource(AbstractField field)
            => IsIntegralSource(field) || field is DecimalField or DoubleField or FloatField;

        public static FunctionField CreateFunctionAggregateField(System.Reflection.PropertyInfo property, Attributes.AggregateFieldAttribute field, AbstractField tablefield)
        {
            var functionName = field switch
            {
                Attributes.AvgFieldAttribute => "AVG",
                Attributes.CountFieldAttribute => "COUNT",
                Attributes.MaxFieldAttribute => "MAX",
                Attributes.MinFieldAttribute => "MIN",
                Attributes.SumFieldAttribute => "SUM",
                _ => throw new NotSupportedException($"Aggregate attribute {field.GetType().Name} is not supported.")
            };

            return CreateFunctionField(property, functionName, tablefield);
        }

        /// <summary>
        /// Creates an aggregate <see cref="FunctionField"/> by SQL function name and source field type.
        /// Shared by both attribute-based views (<see cref="CreateFunctionAggregateField"/>) and
        /// portable view definitions (<see cref="SqlViewTranslator"/>).
        /// </summary>
        /// <remarks>
        /// TASK-887: COUNT, SUM and AVG answer a number, so their view property must be numeric, and SUM and AVG need a
        /// numeric source column; anything else is refused here, by name, rather than when a row is read.
        /// </remarks>
        public static FunctionField CreateFunctionField(System.Reflection.PropertyInfo property, string functionName, AbstractField sourceField)
        {
            FunctionField? functionField = null;
            var parameters = new object[] { sourceField.Name };

            if (functionName is "COUNT" or "SUM" or "AVG")
            {
                if (!IsNumericTarget(property))
                {
                    throw new NotSupportedException(
                        $"View property '{property.DeclaringType?.Name}.{property.Name}' is {property.PropertyType.Name}, and {functionName} "
                        + "answers a number. Declare it as a numeric type. (TASK-887)");
                }
                if (functionName != "COUNT" && !IsNumericSource(sourceField))
                {
                    throw new NotSupportedException(
                        $"{functionName}({sourceField.Name}) for view property '{property.DeclaringType?.Name}.{property.Name}' needs a "
                        + $"numeric column, and '{sourceField.Name}' is {sourceField.Property?.PropertyType.Name ?? sourceField.Type.ToString()}. (TASK-887)");
                }
            }

            if (functionName == "COUNT")
            {
                functionField = sourceField.IsNotNull
                    ? new IntegerFunction(property, functionName, parameters)
                    : new NullableIntegerFunction(property, functionName, parameters);
            }
            else if (functionName == "AVG")
            {
                functionField = sourceField.IsNotNull
                    ? new DecimalFunction(property, functionName, parameters)
                    : new NullableDecimalFunction(property, functionName, parameters);
            }
            else if (functionName is "SUM" or "MIN" or "MAX")
            {
                functionField = CreateTypedFunctionField(property, functionName, parameters, sourceField);
                if (!IsNumericTarget(property))
                {
                    functionField.ValueReader = sourceField.ReadingInto(property);
                }
            }

            if (functionField != null)
            {
                functionField.IsAggregate = true;
                functionField.SourceType = ClrTypeOf(sourceField);
            }

            return functionField!;
        }

        private static FunctionField CreateTypedFunctionField(
            System.Reflection.PropertyInfo property, string functionName, object[] parameters, AbstractField sourceField)
        {
            if (sourceField is IntegerField)
            {
                return sourceField.IsNotNull
                    ? new IntegerFunction(property, functionName, parameters)
                    : new NullableIntegerFunction(property, functionName, parameters);
            }

            if (sourceField is DecimalField)
            {
                return sourceField.IsNotNull
                    ? new DecimalFunction(property, functionName, parameters)
                    : new NullableDecimalFunction(property, functionName, parameters);
            }

            if (sourceField is DateTimeField)
            {
                return sourceField.IsNotNull
                    ? new DateTimeFunction(property, functionName, parameters)
                    : new NullableDateTimeFunction(property, functionName, parameters);
            }

            if (sourceField is BooleanField)
            {
                return sourceField.IsNotNull
                    ? new BooleanFunction(property, functionName, parameters)
                    : new NullableBooleanFunction(property, functionName, parameters);
            }

            if (sourceField is GuidField)
            {
                return sourceField.IsNotNull
                    ? new GuidFunction(property, functionName, parameters)
                    : new NullableGuidFunction(property, functionName, parameters);
            }

            if (sourceField is CharField charField)
            {
                return new CharFunction(property, functionName, parameters, charField.Lenght);
            }

            if (sourceField is StringField)
            {
                return new StringFunction(property, functionName, parameters);
            }

            // TASK-887: every other column (long, short, double, float, a UTC DateTime, TimeOnly…) used to become a
            // StringFunction and was read as text. Its type is the source column's.
            return new FunctionField(property, functionName, parameters, sourceField.Type, sourceField.IsNotNull);
        }
    }
}
