using System;
using System.Linq;

namespace Birko.Models.Users.ViewModels
{
    public class Agenda : Birko.Data.ViewModels.LogViewModel
    {
        public const string NameProperty = "Name";
        public const string DefaultProperty = "Default";
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

        private void Agenda_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { NameProperty, DefaultProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(AgendaObjectProperty);
            }
        }
    }
}
