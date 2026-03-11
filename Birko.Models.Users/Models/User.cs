using System;
using System.Linq;
using Birko.Data.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToUser : Birko.Data.Models.ILoadable<ViewModels.User>
    {
        Guid UserGuid { get; set; }
    }

    [Table("Users")]
    public class User : Birko.Data.Models.AbstractDatabaseLogModel, Birko.Data.Models.ILoadable<ViewModels.User>
    {
        public const string UserRolesSeparator = ",";

        [UniqueField]
        [PrecisionField(256)]
        public string UserName { get; set; }

        public string Roles { get; set; }

        public virtual void LoadFrom(ViewModels.User data)
        {
            base.LoadFrom(data);
            if (data != null)
            {
                UserName = data.UserName;
                Roles = (data.Roles != null && data.Roles.Any(x => !string.IsNullOrEmpty(x)))
                    ? string.Join(UserRolesSeparator, data.Roles.Where(x => !string.IsNullOrEmpty(x)).Distinct().OrderBy(x => x))
                    : null;
            }
        }
    }
}
