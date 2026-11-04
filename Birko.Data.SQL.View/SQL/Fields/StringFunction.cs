using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class StringFunction : FunctionField
    {
        public StringFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters, DbType.String, true)
        {

        }

        public override void Read(object value, DbDataReader reader, int index)
        {
            // TASK-724: MIN / MAX over a group whose every value is NULL is NULL; GetString threw on it.
            Property.SetValue(value, reader.IsDBNull(index) ? null : reader.GetString(index), null);
        }
    }
}
