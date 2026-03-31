using Birko.Data.Models;
using Birko.Data.Sync.Models;
using System;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

public class TestSyncKnowledge : AbstractModel, ISyncKnowledgeItem
{
    public Guid EntityGuid { get; set; }
    public string Scope { get; set; } = string.Empty;
    public DateTime LastSyncedAt { get; set; }
    public string? LocalVersion { get; set; }
    public string? RemoteVersion { get; set; }
    public bool IsLocalDeleted { get; set; }
    public bool IsRemoteDeleted { get; set; }
    public string? Metadata { get; set; }
}
