using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Birko.ViewModels
{
    public interface IProductTags 
        : ILoadable<Models.IProductTags>
        , ILoadable<IProductTags>
    {
        const string TagsProperty = "Tags";
        Dictionary<string, IList<string>> Tags { get; set; }

        new void LoadFrom(IProductTags data)
        {
            Tags.Clear();
            if (data.Tags != null && data.Tags.Any())
            {
                foreach (var tag in data.Tags.Where(x => x.Value.Any()))
                {
                    foreach (var value in tag.Value)
                    {
                        AddTag(tag.Key, value);
                    }
                }
            }
        }
        new void LoadFrom(Models.IProductTags data)
        {
            Tags = new();
            if (data.Tags != null && data.Tags.Any())
            {
                foreach (var property in data.Tags)
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
