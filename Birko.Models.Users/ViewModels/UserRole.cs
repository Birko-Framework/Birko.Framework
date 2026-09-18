using System;

namespace Birko.Models.Users.ViewModels
{
    public class UserRole : Birko.Data.ViewModels.LogViewModel
    {
        public const string TenantGuidProperty = "TenantGuid";
        public const string GrantedAtProperty = "GrantedAt";
        public const string UserRoleObjectProperty = "UserRole";

        public UserRole()
        {
            PropertyChanged += UserRole_PropertyChanged;
        }

        private Guid? _tenantGuid;
        public Guid? TenantGuid
        {
            get { return _tenantGuid; }
            set
            {
                if (_tenantGuid != value)
                {
                    _tenantGuid = value;
                    RaisePropertyChanged(TenantGuidProperty);
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

        // CR-L323: cached watched-property set — avoids a per-event array allocation + LINQ scan.
        private static readonly System.Collections.Generic.HashSet<string> _watchedProperties = new() { TenantGuidProperty, GrantedAtProperty };

        private void UserRole_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserRoleObjectProperty);
            }
        }
    }
}
