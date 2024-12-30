using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models
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
                    ?.Where(x => x.Value != null)
                    ?.ToArray()
                    ?? Array.Empty<SourceValue<string>>();
        }
    }
}
