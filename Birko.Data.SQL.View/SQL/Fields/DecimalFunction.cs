using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class DecimalFunction : FunctionField
    {
        public DecimalFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters, DbType.Decimal, true)
        {

        }
    }

    public class NullableDecimalFunction : DecimalFunction
    {
        public NullableDecimalFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters)
        {
            IsNotNull = false;
        }
    }
}
