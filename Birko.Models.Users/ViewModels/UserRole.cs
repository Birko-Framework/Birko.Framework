using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class UserRole : Birko.Data.ViewModels.LogViewModel
    {
        public const string AgendaGuidProperty = "AgendaGuid";
        public const string GrantedAtProperty = "GrantedAt";
        public const string UserRoleObjectProperty = "UserRole";

        public UserRole()
        {
            PropertyChanged += UserRole_PropertyChanged;
        }

        private Guid? _agendaGuid;
        public Guid? AgendaGuid
        {
            get { return _agendaGuid; }
            set
            {
                if (_agendaGuid != value)
                {
                    _agendaGuid = value;
                    RaisePropertyChanged(AgendaGuidProperty);
                }
            }
        }

        private DateTime _grantedAt = DateTime.UtcNow;
        public DateTime GrantedAt
        {
            get { return _grantedAt; }
            set
            {
                if (_grantedAt != value)
                {
                    _grantedAt = value;
                    RaisePropertyChanged(GrantedAtProperty);
                }
            }
        }

        private void UserRole_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { AgendaGuidProperty, GrantedAtProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserRoleObjectProperty);
            }
        }
    }
}
