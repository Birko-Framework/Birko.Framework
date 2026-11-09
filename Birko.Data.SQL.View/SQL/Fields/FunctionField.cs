using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Text;

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
        /// Converts a numeric aggregate the server answered to the view property's type, exactly, or refuses it.
        /// </summary>
        internal object? ToViewValue(object? raw)
        {
            var declared = Property.PropertyType;
            var target = Nullable.GetUnderlyingType(declared) ?? declared;
            if (raw == null || raw is DBNull)
            {
                if (!declared.IsValueType || Nullable.GetUnderlyingType(declared) != null)
                {
                    return null;
                }
                throw new InvalidCastException(
                    $"{Describe()} answered NULL, and view property '{DescribeProperty()}' ({declared.Name}) cannot hold NULL. "
                    + $"Declare it {target.Name}? to read it. (TASK-887)");
            }

            var numeric = target.IsEnum ? Enum.GetUnderlyingType(target) : target;
            if (!IsNumeric(numeric))
            {
                throw new InvalidCastException(
                    $"{Describe()} is read as a number, and view property '{DescribeProperty()}' is {declared.Name}. (TASK-887)");
            }

            // SQLite keeps a decimal column as text, so its MIN / MAX comes back as that text.
            var value = raw is string text ? decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) : raw;
            object converted;
            try
            {
                converted = IsIntegral(numeric) ? ToIntegral(value, numeric) : Convert.ChangeType(value, numeric, CultureInfo.InvariantCulture);
            }
            catch (OverflowException ex)
            {
                throw new OverflowException(
                    $"{Describe()} answered {Show(raw)}, which is out of range for view property '{DescribeProperty()}' ({declared.Name}). (TASK-887)", ex);
            }
            return target.IsEnum ? Enum.ToObject(target, converted) : converted;
        }

        private object ToIntegral(object value, Type numeric)
        {
            var whole = value switch
            {
                decimal m => m == decimal.Truncate(m),
                double d => !double.IsNaN(d) && !double.IsInfinity(d) && d == Math.Truncate(d),
                float f => !float.IsNaN(f) && !float.IsInfinity(f) && f == MathF.Truncate(f),
                _ => IsIntegral(value.GetType()),
            };
            if (!whole)
            {
                throw new InvalidCastException(
                    $"{Describe()} answered {Show(value)}, which is not a whole number, and view property '{DescribeProperty()}' "
                    + $"is {Property.PropertyType.Name}. Declare it double or decimal to read the fraction. (TASK-887)");
            }
            return Convert.ChangeType(value, numeric, CultureInfo.InvariantCulture);
        }

        private static string Show(object value)
            => value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty;

        private static bool IsIntegral(Type type)
            => type == typeof(sbyte) || type == typeof(byte) || type == typeof(short) || type == typeof(ushort)
               || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);

        private static bool IsNumeric(Type type)
            => IsIntegral(type) || type == typeof(decimal) || type == typeof(double) || type == typeof(float);

        private static bool IsNumericTarget(PropertyInfo property)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            return IsNumeric(type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }

        private static bool IsIntegralSource(AbstractField field) => field is IntegerField or LongField or ShortField;

        private static bool IsNumericSource(AbstractField field)
            => IsIntegralSource(field) || field is DecimalField or DoubleField or FloatField;

        /// <summary>
        /// The operand of an AVG over an integral column: the column times a floating-point one. SQL Server averages an
        /// integral column in its own type, so the AVG of 1 and 4 is 2 there and 2.5 everywhere else and in LINQ (measured
        /// on 2022, TASK-887). <c>1.0E0</c> is a float on SQL Server, MySQL and SQLite and an exact numeric on PostgreSQL,
        /// which already averaged an integer column as numeric.
        /// </summary>
        private const string FractionalOperand = " * 1.0E0";

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
                if (IsIntegralSource(sourceField))
                {
                    parameters = new object[] { sourceField.Name + FractionalOperand };
                }
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
