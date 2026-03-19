using Birko.Data.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.ViewModels
{
    public abstract class LogViewModel : ModelViewModel, ILogEntity, ILoadable<ILogEntity>, ILoadable<LogViewModel>
    {
        public const string CreatedAtProperty = "CreatedAt";
        public const string UpdatedAtProperty = "UpdatedAt";
        public const string PrevUpdatedAtProperty = "PrevUpdatedAt";

        private DateTime _createdAt = DateTime.UtcNow;
        public DateTime CreatedAt
        {
            get { return _createdAt; }
            set
            {
                if (_createdAt != value)
                {
                    _createdAt = value;
                    RaisePropertyChanged(CreatedAtProperty);
                }
            }
        }

        private DateTime _updatedAt = DateTime.UtcNow;
        public DateTime UpdatedAt
        {
            get { return _updatedAt; }
            set
            {
                if (_updatedAt != value)
                {
                    _updatedAt = value;
                    RaisePropertyChanged(UpdatedAtProperty);
                }
            }
        }

        private DateTime? _prevUpdatedAt = null;
        public DateTime? PrevUpdatedAt
        {
            get { return _prevUpdatedAt; }
            set
            {
                if (_prevUpdatedAt != value)
                {
                    _prevUpdatedAt = value;
                    RaisePropertyChanged(PrevUpdatedAtProperty);
                }
            }
        }

        public void LoadFrom(ILogEntity data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }

        public void LoadFrom(LogViewModel data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }
    }
}
