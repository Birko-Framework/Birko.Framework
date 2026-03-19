using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.ViewModels
{
    public abstract class ModelViewModel : ViewModel, IGuidEntity, ILoadable<IGuidEntity>, ILoadable<ModelViewModel>
    {
        public const string GuidProperty = "Guid";

        private Guid? _guid;
        public Guid? Guid
        {
            get { return _guid; }
            set
            {
                if (_guid != value)
                {
                    _guid = value;
                    RaisePropertyChanged(GuidProperty);
                }
            }
        }

        public void LoadFrom(IGuidEntity data)
        {
            if (data != null)
            {
                Guid = data.Guid;
            }
        }

        public void LoadFrom(ModelViewModel data)
        {
            if (data != null)
            {
                Guid = data.Guid;
            }
        }
    }
}
