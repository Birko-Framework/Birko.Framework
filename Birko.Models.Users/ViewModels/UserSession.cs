using System;

namespace Birko.Models.Users.ViewModels
{
    /// <summary>
    /// A signed-in device as a page shows it (TASK-805). It has no token hash, by design: see
    /// <see cref="Models.Users.UserSession"/>.
    /// </summary>
    public class UserSession : Birko.Data.ViewModels.LogViewModel
    {
        public const string TenantGuidProperty = "TenantGuid";
        public const string DeviceLabelProperty = "DeviceLabel";
        public const string LastUsedAtProperty = "LastUsedAt";
        public const string ExpiresAtProperty = "ExpiresAt";
        public const string RevokedAtProperty = "RevokedAt";
        public const string RevokedReasonProperty = "RevokedReason";
        public const string UserSessionObjectProperty = "UserSession";

        public UserSession()
        {
            PropertyChanged += UserSession_PropertyChanged;
        }

        /// <summary>Copies what a page may show: identity, timestamps, tenant, device, state. Never a hash.</summary>
        public void LoadFrom(Models.Users.UserSession data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            TenantGuid = data.TenantGuid;
            DeviceLabel = data.DeviceLabel;
            LastUsedAt = data.LastUsedAt;
            ExpiresAt = data.ExpiresAt;
            RevokedAt = data.RevokedAt;
            RevokedReason = data.RevokedReason;
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

        private string? _deviceLabel;
        public string? DeviceLabel
        {
            get { return _deviceLabel; }
            set
            {
                if (_deviceLabel != value)
                {
                    _deviceLabel = value;
                    RaisePropertyChanged(DeviceLabelProperty);
                }
            }
        }

        private DateTime _lastUsedAt;
        public DateTime LastUsedAt
        {
            get { return _lastUsedAt; }
            set
            {
                if (_lastUsedAt != value)
                {
                    _lastUsedAt = value;
                    RaisePropertyChanged(LastUsedAtProperty);
                }
            }
        }

        private DateTime _expiresAt;
        public DateTime ExpiresAt
        {
            get { return _expiresAt; }
            set
            {
                if (_expiresAt != value)
                {
                    _expiresAt = value;
                    RaisePropertyChanged(ExpiresAtProperty);
                }
            }
        }

        private DateTime? _revokedAt;
        public DateTime? RevokedAt
        {
            get { return _revokedAt; }
            set
            {
                if (_revokedAt != value)
                {
                    _revokedAt = value;
                    RaisePropertyChanged(RevokedAtProperty);
                }
            }
        }

        private string? _revokedReason;
        public string? RevokedReason
        {
            get { return _revokedReason; }
            set
            {
                if (_revokedReason != value)
                {
                    _revokedReason = value;
                    RaisePropertyChanged(RevokedReasonProperty);
                }
            }
        }

        private static readonly System.Collections.Generic.HashSet<string> _watchedProperties = new()
        {
            TenantGuidProperty, DeviceLabelProperty, LastUsedAtProperty, ExpiresAtProperty, RevokedAtProperty, RevokedReasonProperty,
        };

        private void UserSession_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserSessionObjectProperty);
            }
        }
    }
}
