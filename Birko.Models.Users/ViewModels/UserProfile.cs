using System;

namespace Birko.Models.Users.ViewModels
{
    public class UserProfile : Birko.Data.ViewModels.LogViewModel
    {
        public const string FirstNameProperty = "FirstName";
        public const string LastNameProperty = "LastName";
        public const string DisplayNameProperty = "DisplayName";
        public const string PhoneProperty = "Phone";
        public const string AvatarUrlProperty = "AvatarUrl";
        public const string LocaleProperty = "Locale";
        public const string TimeZoneProperty = "TimeZone";
        public const string DateOfBirthProperty = "DateOfBirth";
        public const string BioProperty = "Bio";
        public const string UserProfileObjectProperty = "UserProfile";

        public UserProfile()
        {
            PropertyChanged += UserProfile_PropertyChanged;
        }

        private string? _firstName;
        public string? FirstName
        {
            get { return _firstName; }
            set
            {
                if (_firstName != value)
                {
                    _firstName = value;
                    RaisePropertyChanged(FirstNameProperty);
                }
            }
        }

        private string? _lastName;
        public string? LastName
        {
            get { return _lastName; }
            set
            {
                if (_lastName != value)
                {
                    _lastName = value;
                    RaisePropertyChanged(LastNameProperty);
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

        private string? _phone;
        public string? Phone
        {
            get { return _phone; }
            set
            {
                if (_phone != value)
                {
                    _phone = value;
                    RaisePropertyChanged(PhoneProperty);
                }
            }
        }

        private string? _avatarUrl;
        public string? AvatarUrl
        {
            get { return _avatarUrl; }
            set
            {
                if (_avatarUrl != value)
                {
                    _avatarUrl = value;
                    RaisePropertyChanged(AvatarUrlProperty);
                }
            }
        }

        private string? _locale;
        public string? Locale
        {
            get { return _locale; }
            set
            {
                if (_locale != value)
                {
                    _locale = value;
                    RaisePropertyChanged(LocaleProperty);
                }
            }
        }

        private string? _timeZone;
        public string? TimeZone
        {
            get { return _timeZone; }
            set
            {
                if (_timeZone != value)
                {
                    _timeZone = value;
                    RaisePropertyChanged(TimeZoneProperty);
                }
            }
        }

        private DateTime? _dateOfBirth;
        public DateTime? DateOfBirth
        {
            get { return _dateOfBirth; }
            set
            {
                if (_dateOfBirth != value)
                {
                    _dateOfBirth = value;
                    RaisePropertyChanged(DateOfBirthProperty);
                }
            }
        }

        private string? _bio;
        public string? Bio
        {
            get { return _bio; }
            set
            {
                if (_bio != value)
                {
                    _bio = value;
                    RaisePropertyChanged(BioProperty);
                }
            }
        }

        // CR-L323: cached watched-property set — avoids a per-event array allocation + LINQ scan.
        private static readonly System.Collections.Generic.HashSet<string> _watchedProperties = new() { FirstNameProperty, LastNameProperty, DisplayNameProperty, PhoneProperty, AvatarUrlProperty, LocaleProperty, TimeZoneProperty, DateOfBirthProperty, BioProperty };

        private void UserProfile_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && _watchedProperties.Contains(e.PropertyName))
            {
                RaisePropertyChanged(UserProfileObjectProperty);
            }
        }
    }
}
