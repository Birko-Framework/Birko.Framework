using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class UserAgenda : Birko.Data.ViewModels.LogViewModel
    {
        public const string RolesProperty = "Roles";
        public const string UserAgendaObjectProperty = "UserAgenda";

        public UserAgenda()
        {
            PropertyChanged += UserAgenda_PropertyChanged;
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

        private void UserAgenda_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { RolesProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserAgendaObjectProperty);
            }
        }
    }
}
