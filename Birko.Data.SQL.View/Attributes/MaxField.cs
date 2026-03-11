using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.SQL.Attributes
{
    [System.AttributeUsage(System.AttributeTargets.Property)]
    public class MaxFieldAttribute : AggregateFieldAttribute
    {
        public MaxFieldAttribute(Type modelType, string modelPropertyName, string name = null) : base(modelType, modelPropertyName, name)
        { }
    }
}
