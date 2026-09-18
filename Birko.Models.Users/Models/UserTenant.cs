using System;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public class UserTenant : Birko.Data.Models.AbstractLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserTenant>
        , IRelatedToUser
        , IRelatedToTenant
    {
        public Guid UserGuid { get; set; }
        public Guid TenantGuid { get; set; }

        public bool IsOwner { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data?.Guid is Guid guid) // CR-M226
            {
                UserGuid = guid;
            }
        }

        /// <summary>
        /// CR-M227: the UserTenant view model carries no UserGuid/TenantGuid — those foreign keys are
        /// assigned via <c>LoadFrom(ViewModels.User)</c> / <c>LoadFrom(ViewModels.Tenant)</c>, not this overload.
        /// </summary>
        public virtual void LoadFrom(ViewModels.UserTenant data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            IsOwner = data.IsOwner;
            JoinedAt = data.JoinedAt;
        }

        public virtual void LoadFrom(ViewModels.Tenant data)
        {
            if (data?.Guid is Guid guid) // CR-M226
            {
                TenantGuid = guid;
            }
        }
    }
}
