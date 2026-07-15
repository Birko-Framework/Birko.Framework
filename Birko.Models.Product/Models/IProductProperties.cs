using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Product
{
    public interface IProductProperties
    {
        IEnumerable<SourceValue<string>> Properties { get; set; }

        void LoadProperties(IDictionary<string, IList<string>> data)
        {
            Properties = data
                    ?.SelectMany(x => x.Value.Select(y => new SourceValue<string>()
                    {
                        Source = x.Key,
                        Value = y
                    }))
                    // CR-L316: drop null AND empty, matching the viewmodel-side AddProperty normalization
                    // (the two sides disagreed — model-side filtered null-only).
                    ?.Where(x => !string.IsNullOrEmpty(x.Value))
                    ?.ToArray()
                    ?? Array.Empty<SourceValue<string>>();
        }
    }
}
