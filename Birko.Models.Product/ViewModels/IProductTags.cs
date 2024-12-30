using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.ViewModels
{
    public interface IProductTags
    {
        const string TagsProperty = "Tags";
        Dictionary<string, IList<string>> Tags { get; set; }

        void LoadTags(IDictionary<string, IList<string>> data, bool clear = true)
        {
            if (clear)
            {
                Tags.Clear();
            }
            if (data?.Any() ?? false)
            {
                foreach (var tag in data.Where(x => x.Value.Any()))
                {
                    foreach (var value in tag.Value)
                    {
                        AddTag(tag.Key, value);
                    }
                }
            }
        }

        void LoadTags(IEnumerable<Models.SourceValue<string>> data)
        {
            Tags = new();
            if (data?.Any() ?? false)
            {
                foreach (var property in data)
                {
                    AddTag(property.Source, property.Value);
                }
            }
        }

        void AddTag(string source, string value)
        {
            Tags ??= new();
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
            {
                return;
            }

            source = source.ToLower();
            value = value.ToLower();

            if (!Tags.ContainsKey(source))
            {
                Tags.Add(source, new List<string>());
            }
            if (!Tags[source].Any(x => x.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                Tags[source].Add(value);
            }
        }
    }
}
