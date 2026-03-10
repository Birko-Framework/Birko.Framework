using System;
using System.Linq;

namespace Birko.Models.ViewModels
{
    public class AbstractPercentage : Birko.Data.ViewModels.LogViewModel
    {
        public const string PercentageProperty = "Percentage";
        public const string AbstractPercentageObjectProperty = "AbstractPercentage";

        public AbstractPercentage()
        {
            PropertyChanged += AbstractPercentage_PropertyChanged;
        }

        private decimal _percentage;
        public decimal Percentage
        {
            get { return _percentage; }
            set
            {
                if (_percentage != value)
                {
                    _percentage = value;
                    RaisePropertyChanged(PercentageProperty);
                }
            }
        }

        private void AbstractPercentage_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (new[] { PercentageProperty }.Contains(e.PropertyName))
            {
                RaisePropertyChanged(AbstractPercentageObjectProperty);
            }
        }
    }
}
