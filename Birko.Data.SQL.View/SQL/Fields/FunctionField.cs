using System;
using System.Collections.Generic;
using System.Data;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class FunctionField : AbstractField
    {
        public object[] Parameters { get; set; }
        public FunctionField(System.Reflection.PropertyInfo property, string name, object[] parameters, DbType type = DbType.String, bool notNull = false)
            : base(property, name, type, false, notNull, false, false)
        {
            Parameters = parameters;
        }

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
        public static FunctionField CreateFunctionField(System.Reflection.PropertyInfo property, string functionName, AbstractField sourceField)
        {
            FunctionField? functionField = null;
            var parameters = new object[] { sourceField.Name };

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
            }

            if (functionField != null)
            {
                functionField.IsAggregate = true;
            }

            return functionField!;
        }

        private static FunctionField? CreateTypedFunctionField(
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

            return new StringFunction(property, functionName, parameters);
        }
    }
}
