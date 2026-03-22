using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class UserTenant : Birko.Data.ViewModels.LogViewModel
    {
        public const string IsOwnerProperty = "IsOwner";
        public const string JoinedAtProperty = "JoinedAt";
        public const string UserTenantObjectProperty = "UserTenant";

        public UserTenant()
        {
            PropertyChanged += UserTenant_PropertyChanged;
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

        private void UserTenant_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { IsOwnerProperty, JoinedAtProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserTenantObjectProperty);
            }
        }
    }
}
