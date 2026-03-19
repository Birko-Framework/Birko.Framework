using Birko.Data.Models;
using System;

namespace Birko.Data.ViewModels
{
    public abstract class AbstractLogViewModel : ViewModel, ILogEntity, ILoadable<ILogEntity>, ILoadable<AbstractLogViewModel>
    {
        public const string CreatedAtProperty = "CreatedAt";
        public const string UpdatedAtProperty = "UpdatedAt";
        public const string PrevUpdatedAtProperty = "PrevUpdatedAt";

        private Guid? _guid;
        public Guid? Guid
        {
            get => _guid;
            set
            {
                if (_guid != value)
                {
                    _guid = value;
                    RaisePropertyChanged(nameof(Guid));
                }
            }
        }

        private DateTime _createdAt = DateTime.UtcNow;
        public DateTime CreatedAt
        {
            get => _createdAt;
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
            get => _updatedAt;
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
            get => _prevUpdatedAt;
            set
            {
                if (_prevUpdatedAt != value)
                {
                    _prevUpdatedAt = value;
                    RaisePropertyChanged(PrevUpdatedAtProperty);
                }
            }
        }

        public virtual void LoadFrom(ILogEntity data)
        {
            if (data != null)
            {
                Guid = data.Guid;
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }

        public virtual void LoadFrom(AbstractLogViewModel data)
        {
            if (data != null)
            {
                Guid = data.Guid;
                CreatedAt = data.CreatedAt;
                UpdatedAt = data.UpdatedAt;
                PrevUpdatedAt = data.PrevUpdatedAt;
            }
        }
    }
}
