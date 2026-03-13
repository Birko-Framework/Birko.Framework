using System;

namespace Birko.Models.Users.Filters
{
    public class UserAgenda
    {
        public Guid? UserGuid { get; set; }
        public Guid? AgendaGuid { get; set; }
        public string? Role { get; set; }
    }
}
