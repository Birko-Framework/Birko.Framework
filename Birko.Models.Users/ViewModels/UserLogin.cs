using System;

namespace Birko.Models.Users.ViewModels
{
    public class UserLogin : Birko.Data.ViewModels.LogViewModel
    {
        public const string ProviderProperty = "Provider";
        public const string ProviderKeyProperty = "ProviderKey";
        public const string PasswordHashProperty = "PasswordHash";
        public const string RefreshTokenProperty = "RefreshToken";
        public const string RefreshTokenExpiryProperty = "RefreshTokenExpiry";
        public const string DisplayNameProperty = "DisplayName";
        public const string IsVerifiedProperty = "IsVerified";
        public const string LastUsedAtProperty = "LastUsedAt";
        public const string UserLoginObjectProperty = "UserLogin";

        public UserLogin()
        {
            PropertyChanged += UserLogin_PropertyChanged;
        }

        private string _provider = null!;
        public string Provider
        {
            get { return _provider; }
            set
            {
                if (_provider != value)
                {
                    _provider = value;
                    RaisePropertyChanged(ProviderProperty);
                }
            }
        }

        private string _providerKey = null!;
        public string ProviderKey
        {
            get { return _providerKey; }
            set
            {
                if (_providerKey != value)
                {
                    _providerKey = value;
                    RaisePropertyChanged(ProviderKeyProperty);
                }
            }
        }

        private string? _passwordHash;
        public string? PasswordHash
        {
            get { return _passwordHash; }
            set
            {
                if (_passwordHash != value)
                {
                    _passwordHash = value;
                    RaisePropertyChanged(PasswordHashProperty);
                }
            }
        }

        private string? _refreshToken;
        public string? RefreshToken
        {
            get { return _refreshToken; }
            set
            {
                if (_refreshToken != value)
                {
                    _refreshToken = value;
                    RaisePropertyChanged(RefreshTokenProperty);
                }
            }
        }

        private DateTime? _refreshTokenExpiry;
        public DateTime? RefreshTokenExpiry
        {
            get { return _refreshTokenExpiry; }
            set
            {
                if (_refreshTokenExpiry != value)
                {
                    _refreshTokenExpiry = value;
                    RaisePropertyChanged(RefreshTokenExpiryProperty);
                }
            }
        }

        private string? _displayName;
        public string? DisplayName
        {
            get { return _displayName; }
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    RaisePropertyChanged(DisplayNameProperty);
                }
            }
        }

        private bool _isVerified;
        public bool IsVerified
        {
            get { return _isVerified; }
            set
            {
                if (_isVerified != value)
                {
                    _isVerified = value;
                    RaisePropertyChanged(IsVerifiedProperty);
                }
            }
        }

        private DateTime? _lastUsedAt;
        public DateTime? LastUsedAt
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

        // CR-L323: cached watched-property set — avoids a per-event array allocation + LINQ scan.
        private static readonly System.Collections.Generic.HashSet<string> _watchedProperties = new() { ProviderProperty, ProviderKeyProperty, PasswordHashProperty, RefreshTokenProperty, RefreshTokenExpiryProperty, DisplayNameProperty, IsVerifiedProperty, LastUsedAtProperty };

        private void UserLogin_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserLoginObjectProperty);
            }
        }
    }
}
