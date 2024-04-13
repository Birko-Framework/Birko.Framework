using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Birko.ViewModels
{
    public class SourceValue<T> : Birko.Data.ViewModels.ViewModel, Birko.Data.Models.ILoadable<Models.SourceValue<T>>, Birko.Data.Models.ILoadable<SourceValue<T>>
    {
        public const string SourceProperty = "Source";
        public const string ValueProperty = "Value";
        public const string SourceValueObjectProperty = "SourceValueObject";

        public SourceValue()
        {
            PropertyChanged += SourceValue_PropertyChanged;
        }

        private string _source;
        public string Source
        {
            get { return _source; }
            set
            {
                if (_source != value)
                {
                    _source = value;
                    RaisePropertyChanged(SourceProperty);
                }
            }
        }

        private T _value;
        public T Value
        {
            get { return _value; }
            set
            {
                _value = value;
                RaisePropertyChanged(ValueProperty);
            }
        }

        private void SourceValue_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] {
                    ValueProperty,
                    SourceProperty
                }.Contains(e.PropertyName)
            )
            {
                RaisePropertyChanged(SourceValueObjectProperty);
            }
        }

        public void LoadFrom(Models.SourceValue<T> data)
        {
            if (data != null)
            {
                Source = data.Source;
                Value = data.Value;
            }
        }

        public void LoadFrom(SourceValue<T> data)
        {
            if (data != null)
            {
                Source = data.Source;
                Value = data.Value;
            }
        }
    }
}
