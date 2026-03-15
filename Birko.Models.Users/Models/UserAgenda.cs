using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    [Table("UserAgendas")]
    public class UserAgenda : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.UserAgenda>
        , IRelatedToUser
        , IRelatedToAgenda
    {
        public Guid UserGuid { get; set; }
        public Guid AgendaGuid { get; set; }

        [NamedField("IsOwner")]
        public bool IsOwner { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data != null)
            {
                UserGuid = data.Guid!.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.UserAgenda data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            IsOwner = data.IsOwner;
            JoinedAt = data.JoinedAt;
        }

        public virtual void LoadFrom(ViewModels.Agenda data)
        {
            if (data != null)
            {
                AgendaGuid = data.Guid!.Value;
            }
        }
    }
}
