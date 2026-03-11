using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class User : Birko.Data.ViewModels.LogViewModel
    {
        public const string UserNameProperty = "UserName";
        public const string RolesProperty = "Roles";
        public const string UserObjectProperty = "User";

        public User()
        {
            PropertyChanged += User_PropertyChanged;
        }

        private string _userName;
        public string UserName
        {
            get { return _userName; }
            set
            {
                if (_userName != value)
                {
                    _userName = value;
                    RaisePropertyChanged(UserNameProperty);
                }
            }
        }

        private IEnumerable<string> _roles;
        public IEnumerable<string> Roles
        {
            get { return _roles; }
            set
            {
                _roles = value;
                RaisePropertyChanged(RolesProperty);
            }
        }

        private void User_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { UserNameProperty, RolesProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserObjectProperty);
            }
        }
    }
}
