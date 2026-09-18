using System;

namespace Birko.Models.Users.ViewModels
{
    public class User : Birko.Data.ViewModels.LogViewModel
    {
        public const string UserNameProperty = "UserName";
        public const string EmailProperty = "Email";
        public const string IsActiveProperty = "IsActive";
        public const string LastLoginAtProperty = "LastLoginAt";
        public const string EmailVerifiedProperty = "EmailVerified";
        public const string UserObjectProperty = "User";

        public User()
        {
            PropertyChanged += User_PropertyChanged;
        }

        private string _userName = null!;
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

        private string? _email;
        public string? Email
        {
            get { return _email; }
            set
            {
                if (_email != value)
                {
                    _email = value;
                    RaisePropertyChanged(EmailProperty);
                }
            }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    RaisePropertyChanged(IsActiveProperty);
                }
            }
        }

        private DateTime? _lastLoginAt;
        public DateTime? LastLoginAt
        {
            get { return _lastLoginAt; }
            set
            {
                if (_lastLoginAt != value)
                {
                    _lastLoginAt = value;
                    RaisePropertyChanged(LastLoginAtProperty);
                }
            }
        }

        private bool _emailVerified;
        public bool EmailVerified
        {
            get { return _emailVerified; }
            set
            {
                if (_emailVerified != value)
                {
                    _emailVerified = value;
                    RaisePropertyChanged(EmailVerifiedProperty);
                }
            }
        }

        // CR-L323: cached watched-property set — avoids a per-event array allocation + LINQ scan.
        private static readonly System.Collections.Generic.HashSet<string> _watchedProperties = new() { UserNameProperty, EmailProperty, IsActiveProperty, LastLoginAtProperty, EmailVerifiedProperty };

        private void User_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserObjectProperty);
            }
        }
    }
}
