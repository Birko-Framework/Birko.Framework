using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models
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
                    ?.Where(x => x.Value != null)
                    ?.ToArray()
                    ?? Array.Empty<SourceValue<string>>();
        }
    }
}
