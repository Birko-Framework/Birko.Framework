using System;
using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Models.Users
{
    public interface IRelatedToAgenda : Birko.Data.Models.ILoadable<ViewModels.Agenda>
    {
        Guid AgendaGuid { get; set; }
    }

    [Table("Agendas")]
    public class Agenda
        : Birko.Data.Models.AbstractDatabaseLogModel
        , Birko.Data.Models.ILoadable<ViewModels.Agenda>
        , IDefault
    {
        [PrecisionField(256)]
        public string Name { get; set; } = null!;

        [NamedField("IsDefault")]
        public bool Default { get; set; } = false;

        public virtual void LoadFrom(ViewModels.Agenda data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Default = data.Default;
        }
    }
}
