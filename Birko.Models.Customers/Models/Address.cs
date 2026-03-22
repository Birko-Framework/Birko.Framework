using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Customers
{
    public interface IRelatedToAddress : Birko.Data.Models.ILoadable<ViewModels.Address>
    {
        Guid? AddressGuid { get; set; }
    }

    [Table("Addresses")]
    public class Address
        : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.Address>
        , ICopyable<Address>
        , Birko.Models.Contracts.IAddressable
        , Birko.Models.Contracts.IContactable
    {
        public string Name { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string StreetNumber { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string ZIP { get; set; } = string.Empty;
        public string District { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public virtual Address CopyTo(Address clone)
        {
            if (clone == null)
            {
                clone = new Address();
            }
            clone = (Address)base.CopyTo(clone);
            clone.Name = Name;
            clone.Street = Street;
            clone.StreetNumber = StreetNumber;
            clone.City = City;
            clone.ZIP = ZIP;
            clone.District = District;
            clone.Region = Region;
            clone.Country = Country;
            clone.Phone = Phone;
            clone.Email = Email;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.Address data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Street = data.Street;
            StreetNumber = data.StreetNumber;
            City = data.City;
            ZIP = data.ZIP;
            District = data.District;
            Region = data.Region;
            Country = data.Country;
            Phone = data.Phone;
            Email = data.Email;
        }
    }
}
