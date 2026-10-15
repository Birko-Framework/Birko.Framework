using System;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.Patterns.Decorators;
using Birko.Data.Patterns.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-M317 (TASK-159). The Audit decorator adds <c>Set(x =&gt; x.UpdatedBy, …)</c> with T constrained to
/// <c>IAuditable</c>, so the member binds to the INTERFACE property, which has no <c>[NamedField]</c>: the SET named
/// <c>UpdatedBy</c> on a table whose column is <c>updated_by</c>. Measured on a real database, by the stored value.
/// </summary>
public class InterfaceMemberColumnRemapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-remap-{Guid.NewGuid():N}");

    public InterfaceMemberColumnRemapTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqlitePool.ClearForDirectory(_root, 2);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private sealed class FixedUser : IAuditContext
    {
        public Guid? CurrentUserGuid { get; } = Guid.NewGuid();
    }

    [Table("Remap159")]
    public class Doc : AbstractModel, IAuditable
    {
        public string? Name { get; set; }

        [NamedField("created_by")]
        public Guid? CreatedBy { get; set; }

        [NamedField("updated_by")]
        public Guid? UpdatedBy { get; set; }
    }

    [Fact]
    public void An_audited_filtered_update_writes_the_remapped_column()
    {
        var store = new DataBaseBulkStore<SqLiteConnector, Doc>();
        store.SetSettings(new SqLiteSettings(_root, "remap.db"));
        store.Create(new Doc { Name = "a" });
        var user = new FixedUser();
        var audited = new AuditBulkStoreWrapper<DataBaseBulkStore<SqLiteConnector, Doc>, Doc>(store, user);

        audited.Update(x => x.Name == "a", new PropertyUpdate<Doc>().Set(x => x.Name, "b"));

        var row = store.ReadFirst(x => x.Name == "b");
        row.Should().NotBeNull();
        row!.UpdatedBy.Should().Be(user.CurrentUserGuid);
    }
}
