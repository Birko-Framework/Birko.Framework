using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Product
{
    public interface IProductTags
    {
        IEnumerable<SourceValue<string>> Tags { get; set; }

        void LoadTags(IDictionary<string, IList<string>> data)
        {
            Tags = data
                    ?.SelectMany(x => x.Value.Select(y => new SourceValue<string>()
                    {
                        Source = x.Key,
                        Value = y
                    }))
                    // CR-L316: drop null AND empty, matching the viewmodel-side AddTag normalization
                    // (the two sides disagreed — model-side filtered null-only).
                    ?.Where(x => !string.IsNullOrEmpty(x.Value))
                    ?.ToArray()
                    ?? Array.Empty<SourceValue<string>>();
        }
    }
}
