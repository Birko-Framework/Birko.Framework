using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class RolePermission : Birko.Data.ViewModels.LogViewModel
    {
        public const string PermissionCodeProperty = "PermissionCode";
        public const string GrantedAtProperty = "GrantedAt";
        public const string RolePermissionObjectProperty = "RolePermission";

        public RolePermission()
        {
            PropertyChanged += RolePermission_PropertyChanged;
        }

        private string _permissionCode = null!;
        public string PermissionCode
        {
            get { return _permissionCode; }
            set
            {
                if (_permissionCode != value)
                {
                    _permissionCode = value;
                    RaisePropertyChanged(PermissionCodeProperty);
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

        private void RolePermission_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { PermissionCodeProperty, GrantedAtProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(RolePermissionObjectProperty);
            }
        }
    }
}
