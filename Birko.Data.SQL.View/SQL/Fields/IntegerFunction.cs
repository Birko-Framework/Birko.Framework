using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class IntegerFunction : FunctionField
    {
        public IntegerFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters, DbType.Int32, true)
        {

        }
    }

    public class NullableIntegerFunction : IntegerFunction
    {
        public NullableIntegerFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters)
        {
            IsNotNull = false;
        }
    }
}
