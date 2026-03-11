using System;
using System.Linq;
using Birko.Data.Attributes;
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
        public string Roles { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            if (data != null)
            {
                UserGuid = data.Guid.Value;
            }
        }

        public virtual void LoadFrom(ViewModels.UserAgenda data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                Roles = (data.Roles != null && data.Roles.Any(x => !string.IsNullOrEmpty(x)))
                    ? string.Join(User.UserRolesSeparator, data.Roles.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
                    : null;
            }
        }

        public virtual void LoadFrom(ViewModels.Agenda data)
        {
            if (data != null)
            {
                AgendaGuid = data.Guid.Value;
            }
        }
    }
}
