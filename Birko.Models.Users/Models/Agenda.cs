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

        [PrecisionField(1000)]
        public string? Description { get; set; }

        [NamedField("IsDefault")]
        public bool Default { get; set; } = false;

        [NamedField("IsActive")]
        public bool IsActive { get; set; } = true;

        public virtual void LoadFrom(ViewModels.Agenda data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Description = data.Description;
            Default = data.Default;
            IsActive = data.IsActive;
        }
    }
}
