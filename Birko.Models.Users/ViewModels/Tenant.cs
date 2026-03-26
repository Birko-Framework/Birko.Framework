using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class Tenant : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string DescriptionProperty = "Description";
        public const string DefaultProperty = "IsDefault";
        public const string IsActiveProperty = "IsActive";
        public const string TenantObjectProperty = "Tenant";

        public Tenant()
        {
            PropertyChanged += Tenant_PropertyChanged;
        }

        private string _name = null!;
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

        private string? _description;
        public string? Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChanged(DescriptionProperty);
                }
            }
        }

        private bool _isDefault;
        public bool IsDefault
        {
            get { return _isDefault; }
            set
            {
                if (_isDefault != value)
                {
                    _isDefault = value;
                    RaisePropertyChanged(DefaultProperty);
                }
            }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    RaisePropertyChanged(IsActiveProperty);
                }
            }
        }

        private void Tenant_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, DescriptionProperty, DefaultProperty, IsActiveProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(TenantObjectProperty);
            }
        }
    }
}
