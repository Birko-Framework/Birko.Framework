using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class CharFunction : StringFunction
    {
        public int? Lenght = 1;

        public CharFunction(System.Reflection.PropertyInfo property, string name, object[] parameters, int? lenght = 1)
            : base(property, name, parameters)
        {
            Lenght = lenght;
        }
    }
}
