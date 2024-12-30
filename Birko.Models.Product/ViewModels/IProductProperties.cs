using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.ViewModels
{
    public interface IProductProperties
    {
        const string PropertiesProperty = "Properties";
        Dictionary<string, IList<string>> Properties { get; set; }

        void LoadProperties(IDictionary<string, IList<string>> data, bool clear = true)
        {
            if (clear)
            {
                Properties.Clear();
            }
            if (data?.Any() ?? false)
            {
                foreach (var property in data.Where(x => x.Value.Any()))
                {
                    foreach (var value in property.Value)
                    {
                        AddProperty(property.Key, value);
                    }
                }
            }
        }

        void LoadProperties(IEnumerable<Models.SourceValue<string>> data)
        {
            Properties = new ();
            if (data?.Any() ?? false)
            {
                foreach (var property in data)
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
