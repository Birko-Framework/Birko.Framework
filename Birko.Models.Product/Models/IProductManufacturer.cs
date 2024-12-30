using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models
{
    public interface IProductManufacturer
    {
        IEnumerable<string> Manufacturer { get; set; }

        void LoadManufacturers(IEnumerable<string> data)
        {
            Manufacturer = data?
                .Select(x => x)?
                .Where(x => !string.IsNullOrEmpty(x))?
                .Distinct()?
                .ToArray()
                ?? Array.Empty<string>();
        }
    }
}
