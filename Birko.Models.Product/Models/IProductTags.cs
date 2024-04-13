using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;

namespace Birko.Models
{
    public interface IProductTags
        : ILoadable<ViewModels.IProductTags>
    {
        SourceValue<string>[] Tags { get; set; }

        new void LoadFrom(ViewModels.IProductTags data)
        {
            Tags = data?.Tags
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
