using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class UserAgenda : Birko.Data.ViewModels.LogViewModel
    {
        public const string RolesProperty = "Roles";
        public const string IsOwnerProperty = "IsOwner";
        public const string JoinedAtProperty = "JoinedAt";
        public const string UserAgendaObjectProperty = "UserAgenda";

        public UserAgenda()
        {
            PropertyChanged += UserAgenda_PropertyChanged;
        }

        private IEnumerable<string>? _roles;
        public IEnumerable<string>? Roles
        {
            get { return _roles; }
            set
            {
                _roles = value;
                RaisePropertyChanged(RolesProperty);
            }
        }

        private bool _isOwner;
        public bool IsOwner
        {
            get { return _isOwner; }
            set
            {
                if (_isOwner != value)
                {
                    _isOwner = value;
                    RaisePropertyChanged(IsOwnerProperty);
                }
            }
        }

        private DateTime _joinedAt = DateTime.UtcNow;
        public DateTime JoinedAt
        {
            get { return _joinedAt; }
            set
            {
                if (_joinedAt != value)
                {
                    _joinedAt = value;
                    RaisePropertyChanged(JoinedAtProperty);
                }
            }
        }

        private void UserAgenda_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { RolesProperty, IsOwnerProperty, JoinedAtProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserAgendaObjectProperty);
            }
        }
    }
}
