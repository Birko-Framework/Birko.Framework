using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class Role : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string DescriptionProperty = "Description";
        public const string IsSystemProperty = "IsSystem";
        public const string RoleObjectProperty = "Role";

        public Role()
        {
            PropertyChanged += Role_PropertyChanged;
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

        private bool _isSystem;
        public bool IsSystem
        {
            get { return _isSystem; }
            set
            {
                if (_isSystem != value)
                {
                    _isSystem = value;
                    RaisePropertyChanged(IsSystemProperty);
                }
            }
        }

        private void Role_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, DescriptionProperty, IsSystemProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(RoleObjectProperty);
            }
        }
    }
}
