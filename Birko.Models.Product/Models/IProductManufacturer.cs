using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;

namespace Birko.Models
{
    public interface IProductManufacturer 
        : ILoadable<ViewModels.IProductManufacturer>
    {
        string[] Manufacturer { get; set; }

        new void LoadFrom(ViewModels.IProductManufacturer data)
        {
            Manufacturer = data?.Manufacturer?
                .Select(x => x)?
                .Where(x => x != null)?
                .Distinct()?
                .ToArray() 
                ?? Array.Empty<string>();
        }
    }
}
