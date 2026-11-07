using System;
using System.IO;
using System.Linq;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-836 on SQLite's own collection: the generated IN-list names skip the name lookup, the provider's Guid
/// conversion still applies on that path, and a re-added name still overwrites.
/// </summary>
public class Task836AddParameterTests
{
    private static readonly Connectors.SqLiteConnector Connector =
        new(new SqLiteSettings(Path.GetTempPath(), "t836-unused.db"));

    [Fact]
    public void A_guid_in_list_binds_strings_once_per_value()
    {
        using var command = new SqliteCommand();
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

        Connector.ConditionDefinition(Condition.Create("Guid", ids.Cast<object>().ToArray(), ConditionType.In), command);

        command.Parameters.Cast<SqliteParameter>().Select(p => p.Value)
            .Should().Equal(ids.Select(x => (object)x.ToString()));
    }

    [Fact]
    public void A_name_added_twice_is_overwritten_not_duplicated()
    {
        using var command = new SqliteCommand();
        var second = Guid.NewGuid();

        Connector.AddParameter(command, "@Guid", Guid.NewGuid());
        Connector.AddParameter(command, "@Guid", second);

        command.Parameters.Count.Should().Be(1);
        command.Parameters["@Guid"].Value.Should().Be(second.ToString());
    }
}
