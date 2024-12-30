using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Birko.ViewModels
{
    public interface IProductManufacturer
    {
        const string ManufacturerProperty = "Manufacturer";
        ObservableCollection<string> Manufacturer { get; set; }

        void LoadManufacturers(IEnumerable<string> data, bool clear = true)
        {
            if (clear)
            {
                Manufacturer.Clear();
            }
            if (data?.Any() ?? false)
            {
                foreach (var manufacturer in data)
                {
                   AddManufacturer(manufacturer);
                }
            }
        }

        void AddManufacturer(string manufacturer)
        {
            if (Manufacturer.Contains(manufacturer, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
            Manufacturer.Add(manufacturer);
        }
    }
}
