using Xunit;

namespace Birko.Data.Migrations.TimescaleDB.Tests;

/// <summary>
/// Serialises the live classes that share one TimescaleDB database.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ <b>Diagnosed, not guessed.</b> A full-suite sweep produced
/// <c>QualifiedNameEmitterLiveTests.Two_schemas_holding_the_same_table_name_get_their_own_answers</c>
/// failing with:
/// </para>
/// <code>
/// Npgsql.PostgresException : 42P01: relation
///     "_timescaledb_internal._materialized_hypertable_1048" does not exist
/// </code>
/// <para>
/// That relation is a <b>continuous aggregate's internal table</b>, and this class creates none — so it
/// was dropped by a <i>parallel sibling</i> while this test's read of <c>timescaledb_information</c> was
/// resolving it. xUnit runs test classes in parallel by default, five classes here read those catalogue
/// views, and three of them create and drop materialized views against the same database.
/// </para>
/// <para>
/// <b>Serialising these classes, not disabling parallelism.</b> [[TASK-276]] is explicit that
/// <c>"parallelizeTestCollections": false</c> is the wrong fix — it would hide this here and leave every
/// other suite exposed. A shared collection is the narrow version: exactly the classes that share
/// TimescaleDB catalogue state stop overlapping, and everything else in the project still runs in
/// parallel.
/// </para>
/// <para>
/// Same family as the MSSql instance recorded on TASK-276: parallel classes sharing one server, where one
/// class's DDL invalidates a catalogue read another is mid-query on. The difference is that this one was
/// captured with a trx logger and diagnosed from its message rather than inferred.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class TimescaleDbLiveCollection
{
    public const string Name = "TimescaleDB live";
}
