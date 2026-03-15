using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class Agenda : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string DescriptionProperty = "Description";
        public const string DefaultProperty = "Default";
        public const string IsActiveProperty = "IsActive";
        public const string AgendaObjectProperty = "Agenda";

        public Agenda()
        {
            PropertyChanged += Agenda_PropertyChanged;
        }

        private string _name = null!;
        public string Name
        {
            get { return _name; }
            set
            {
                if (_name != value)
                {
                    _name = value;
                    RaisePropertyChanged(NameProperty);
                }
            }
        }

        private string? _description;
        public string? Description
        {
            get { return _description; }
            set
            {
                if (_description != value)
                {
                    _description = value;
                    RaisePropertyChanged(DescriptionProperty);
                }
            }
        }

        private bool _default;
        public bool Default
        {
            get { return _default; }
            set
            {
                if (_default != value)
                {
                    _default = value;
                    RaisePropertyChanged(DefaultProperty);
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

        private void Agenda_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, DescriptionProperty, DefaultProperty, IsActiveProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(AgendaObjectProperty);
            }
        }
    }
}
