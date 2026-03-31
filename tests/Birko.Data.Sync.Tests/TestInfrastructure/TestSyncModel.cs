using Birko.Data.Models;
using System;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

public class TestSyncModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
