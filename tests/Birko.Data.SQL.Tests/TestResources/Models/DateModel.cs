using Birko.Data.Attributes;
using Birko.Data.Models;
using System;
using System.Collections.Generic;

namespace Birko.Data.SQL.Tests.TestResources.Models
{
    [Table("DateModels")]
    public class DateModel : AbstractLogModel
    {
        public bool IsTest { get; set; } = true;
        public string Text { get; set; }
        public int Count { get; set; } = 0;
        public decimal? Amount { get; set; } = 0;
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }

    public class NestedDateModel : DateModel
    {
        public IEnumerable<DateModel> Nested { get; set; }
    }
}
