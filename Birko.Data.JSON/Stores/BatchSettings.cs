using System;
using System.Collections.Generic;
using System.Text;

namespace Birko.Data.Stores
{
    public class BatchSettings : Settings
    {
        public int BatchSize { get; set; } = 1024;
    }
}
