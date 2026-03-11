using System;
using System.Linq;

namespace Birko.Models.Customers.ViewModels
{
    public class Address : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string StreetProperty = "Street";
        public const string StreetNumberProperty = "StreetNumber";
        public const string CityProperty = "City";
        public const string ZIPProperty = "ZIP";
        public const string DistrictProperty = "District";
        public const string RegionProperty = "Region";
        public const string CountryProperty = "Country";
        public const string PhoneProperty = "Phone";
        public const string EmailProperty = "Email";
        public const string AddressObjectProperty = "Address";

        public Address()
        {
            PropertyChanged += Address_PropertyChanged;
        }

        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChanged(NameProperty);
                }
            }
        }

        private string _street;
        public string Street
        {
            get { return _street; }
            set
            {
                if (_street != value)
                {
                    _street = value;
                    RaisePropertyChanged(StreetProperty);
                }
            }
        }

        private string _streetNumber;
        public string StreetNumber
        {
            get { return _streetNumber; }
            set
            {
                if (_streetNumber != value)
                {
                    _streetNumber = value;
                    RaisePropertyChanged(StreetNumberProperty);
                }
            }
        }

        private string _city;
        public string City
        {
            get { return _city; }
            set
            {
                if (_city != value)
                {
                    _city = value;
                    RaisePropertyChanged(CityProperty);
                }
            }
        }

        private string _zip;
        public string ZIP
        {
            get { return _zip; }
            set
            {
                if (_zip != value)
                {
                    _zip = value;
                    RaisePropertyChanged(ZIPProperty);
                }
            }
        }

        private string _district;
        public string District
        {
            get { return _district; }
            set
            {
                if (_district != value)
                {
                    _district = value;
                    RaisePropertyChanged(DistrictProperty);
                }
            }
        }

        private string _region;
        public string Region
        {
            get { return _region; }
            set
            {
                if (_region != value)
                {
                    _region = value;
                    RaisePropertyChanged(RegionProperty);
                }
            }
        }

        private string _country;
        public string Country
        {
            get { return _country; }
            set
            {
                if (_country != value)
                {
                    _country = value;
                    RaisePropertyChanged(CountryProperty);
                }
            }
        }

        private string _phone;
        public string Phone
        {
            get { return _phone; }
            set
            {
                if (_phone != value)
                {
                    _phone = value;
                    RaisePropertyChanged(PhoneProperty);
                }
            }
        }

        private string _email;
        public string Email
        {
            get { return _email; }
            set
            {
                if (_email != value)
                {
                    _email = value;
                    RaisePropertyChanged(EmailProperty);
                }
            }
        }

        private void Address_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, StreetProperty, StreetNumberProperty, CityProperty, ZIPProperty, DistrictProperty, RegionProperty, CountryProperty, PhoneProperty, EmailProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(AddressObjectProperty);
            }
        }
    }
}
