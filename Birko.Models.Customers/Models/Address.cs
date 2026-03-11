using System;
using Birko.Data.Attributes;
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
    {
        public string Name { get; set; }
        public string Street { get; set; }
        public string StreetNumber { get; set; }
        public string City { get; set; }
        public string ZIP { get; set; }
        public string District { get; set; }
        public string Region { get; set; }
        public string Country { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }

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
            if (data != null)
            {
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
}
