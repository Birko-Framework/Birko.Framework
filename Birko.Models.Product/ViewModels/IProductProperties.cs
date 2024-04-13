using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;

namespace Birko.ViewModels
{
    public interface IProductProperties 
        : ILoadable<Models.IProductProperties>
        , ILoadable<IProductProperties>
    {
        const string PropertiesProperty = "Properties";
        Dictionary<string, IList<string>> Properties { get; set; }

        new void LoadFrom(IProductProperties data)
        {
            Properties.Clear();
            if (data.Properties != null && data.Properties.Any())
            {
                foreach (var property in data.Properties.Where(x => x.Value.Any()))
                {
                    foreach (var value in property.Value)
                    {
                        AddProperty(property.Key, value);
                    }
                }
            }
        }

        new void LoadFrom(Models.IProductProperties data)
        {
            Properties = new ();
            if (data.Properties != null && data.Properties.Any())
            {
                foreach (var property in data.Properties)
                {
                    AddProperty(property.Source, property.Value);
                }
            }
        }

        void AddProperty(string source, string value)
        {
            Properties ??= new();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return;
            }

            source = source.ToLower();
            value = value.ToLower();

            if (!Properties.ContainsKey(source))
            {
                Properties.Add(source, new List<string>());
            }
            if (!Properties[source].Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                Properties[source].Add(value);
            }
        }
    }
}
