using System;
using System.Linq;

namespace Birko.Models.Customers.ViewModels
{
    public class ContactPerson : Birko.Data.ViewModels.LogViewModel
    {
        public const string CustomerGuidProperty = "CustomerGuid";
        public const string NameProperty = "Name";
        public const string PositionProperty = "Position";
        public const string PhoneProperty = "Phone";
        public const string EmailProperty = "Email";
        public const string IsPrimaryProperty = "IsPrimary";

        private Guid? _customerGuid;
        public Guid? CustomerGuid
        {
            get { return _customerGuid; }
            set { if (_customerGuid != value) { _customerGuid = value; RaisePropertyChanged(CustomerGuidProperty); } }
        }

        private string _name = string.Empty;
        public string Name
        {
            get { return _name; }
            set { if (_name != value) { _name = value; RaisePropertyChanged(NameProperty); } }
        }

        private string _position = string.Empty;
        public string Position
        {
            get { return _position; }
            set { if (_position != value) { _position = value; RaisePropertyChanged(PositionProperty); } }
        }

        private string _phone = string.Empty;
        public string Phone
        {
            get { return _phone; }
            set { if (_phone != value) { _phone = value; RaisePropertyChanged(PhoneProperty); } }
        }

        private string _email = string.Empty;
        public string Email
        {
            get { return _email; }
            set { if (_email != value) { _email = value; RaisePropertyChanged(EmailProperty); } }
        }

        private bool _isPrimary;
        public bool IsPrimary
        {
            get { return _isPrimary; }
            set { if (_isPrimary != value) { _isPrimary = value; RaisePropertyChanged(IsPrimaryProperty); } }
        }
    }
}
