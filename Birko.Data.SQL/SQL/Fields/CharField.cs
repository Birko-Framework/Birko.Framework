using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;

namespace Birko.Data.SQL.Fields
{
    public class CharField : StringField
    {
        public int? Lenght = 1;
        public CharField(System.Reflection.PropertyInfo property, string name, bool primary = false, bool unique = false, int? lenght = 1)
            : base(property, name, primary, unique)
        {
            Lenght = lenght;
        }

        public override void Read(object value, DbDataReader reader, int index)
        {
            if (reader.IsDBNull(index))
            {
                Property.SetValue(value, null, null);
            }
            else
            {
                // SH-M221 (TASK-154): a `char` property was handed the column's string and SetValue threw for every
                // row. CharField also serves `string` properties with a length, which keep the string.
                var text = reader.GetString(index);
                var target = Nullable.GetUnderlyingType(Property.PropertyType) ?? Property.PropertyType;
                Property.SetValue(value, target == typeof(char) ? (text.Length > 0 ? text[0] : '\0') : text, null);
            }
        }
    }
}
