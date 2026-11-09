using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class BooleanFunction : FunctionField
    {
        public BooleanFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters, DbType.Boolean, true)
        {

        }
    }

    public class NullableBooleanFunction : BooleanFunction
    {
        public NullableBooleanFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters)
        {
            IsNotNull = false;
        }
    }
}
