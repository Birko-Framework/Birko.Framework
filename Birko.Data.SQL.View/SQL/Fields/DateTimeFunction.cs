using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class DateTimeFunction : FunctionField
    {
        public DateTimeFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters, DbType.DateTime, true)
        {

        }
    }

    public class NullableDateTimeFunction : DateTimeFunction
    {
        public NullableDateTimeFunction(System.Reflection.PropertyInfo property, string name, object[] parameters)
            : base(property, name, parameters)
        {
            IsNotNull = false;
        }
    }
}
