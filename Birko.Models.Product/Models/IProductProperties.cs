using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.Models
{
    public interface IProductProperties
        : ILoadable<ViewModels.IProductProperties>
    {
        SourceValue<string>[] Properties { get; set; }

        new void LoadFrom(ViewModels.IProductProperties data)
        {
            Properties = data?.Properties
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
