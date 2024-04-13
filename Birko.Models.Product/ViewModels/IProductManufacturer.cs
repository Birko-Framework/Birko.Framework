using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Birko.ViewModels
{
    public interface IProductManufacturer 
        : ILoadable<Models.IProductManufacturer>
        , ILoadable<IProductManufacturer>
    {
        const string ManufacturerProperty = "Manufacturer";
        ObservableCollection<string> Manufacturer { get; set; }

        new void LoadFrom(IProductManufacturer data)
        {
            Manufacturer.Clear();
            if (data.Manufacturer != null && data.Manufacturer.Any())
            {
                foreach (var manufacturer in data.Manufacturer)
                {
                    if(Manufacturer.Contains(manufacturer, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    Manufacturer.Add(manufacturer);
                }
            }
        }

        new void LoadFrom(Models.IProductManufacturer data)
        {
            Manufacturer = new(data.Manufacturer);
        }
    }
}
