using System;
using System.IO;
using Birko.Data.Migrations.SQL.Context;
using Birko.Data.Patterns.Schema;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using Birko.Data.SQL.MySQL.Stores;
using Birko.Data.SQL.PostgreSQL.Stores;
using Birko.Data.SQL.SqLite.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// <b>TASK-264 — a migration's declared column metadata reaches the emitted column.</b>
///
/// <para>
/// <c>SchemaField</c> forwarded 5 of <see cref="FieldDescriptor"/>'s 15 properties. The connectors read a
/// column's size off the field's <i>runtime type</i> — <c>field is CharField</c> before a length,
/// <c>field is DecimalField AND Precision != null AND Scale != null</c> before a precision — and
/// <c>SchemaField</c> derived straight from <c>AbstractField</c>, so it satisfied neither test.
/// <c>SchemaField.For</c> now dispatches to the subclass that carries the metadata.
/// </para>
///
/// <para>
/// These assertions are offline on purpose: <c>FieldDefinition</c> is pure string generation, so all four
/// providers are measurable without a server, and the emitted DDL is exactly what the defect was about.
/// The live half is <see cref="SqliteMigrationColumnMetadataTests"/>.
/// </para>
/// </summary>
public class MigrationColumnMetadataTests
{
    private static AbstractConnector SqLite()
        => DataBase.GetConnector<SqLiteConnector>(new SqLiteSettings(Path.GetTempPath(), "task264-offline.db"));
    private static AbstractConnector Postgres()
        => DataBase.GetConnector<PostgreSQLConnector>(new PostgreSqlSettings());
    private static AbstractConnector MSSql()
        => DataBase.GetConnector<MSSqlConnector>(new MSSqlSettings());
    private static AbstractConnector MySql()
        => DataBase.GetConnector<MySQLConnector>(new MySqlSettings());

    private static string Definition(AbstractConnector connector, FieldDescriptor descriptor)
        => connector.FieldDefinition(SchemaField.For(descriptor));

    private static AbstractConnector ConnectorNamed(string provider) => provider switch
    {
        "SQLite" => SqLite(),
        "PostgreSQL" => Postgres(),
        "MSSql" => MSSql(),
        "MySQL" => MySql(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "unknown provider")
    };

    // ───────────────────────────── MaxLength (the filed half) ─────────────────────────────

    /// <summary>
    /// A declared <c>maxLength</c> reaches the column on every provider that has a bounded string type.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>SQLite is deliberately <c>TEXT</c>, and that is not a gap.</b> SQLite has no length-enforcing
    /// string type — a <c>VARCHAR(50)</c> declaration there is TEXT affinity with the number ignored — so
    /// its <c>ConvertType</c> maps every <c>DbType.String</c> to <c>TEXT</c> whether the field carries a
    /// length or not. The task's criterion asked for the bounded type on "all four providers"; three is
    /// the honest answer, and asserting <c>TEXT</c> here is what stops a later reader "fixing" SQLite into
    /// a divergence from its own convention.
    /// </remarks>
    [Theory]
    [InlineData("SQLite", "C TEXT")]
    [InlineData("PostgreSQL", "C VARCHAR(50)")]
    [InlineData("MSSql", "C NVARCHAR(50)")]
    [InlineData("MySQL", "C VARCHAR(50)")]
    public void A_declared_maxLength_reaches_the_column(string provider, string expected)
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.String, MaxLength = 50 };

        Definition(ConnectorNamed(provider), descriptor).Should().Be(expected,
            "before TASK-264 SchemaField was not a CharField, so ConvertType could not see the length and "
            + "emitted the unbounded type");
    }

    /// <summary>
    /// The other side of the same switch: with no length declared the column is still unbounded. Without
    /// this the fix would be indistinguishable from bounding every migration string, which would impose a
    /// ceiling on values that write fine today (§ TASK-248's rule about the loud narrow failure).
    /// </summary>
    [Theory]
    [InlineData("SQLite", "C TEXT")]
    [InlineData("PostgreSQL", "C TEXT")]
    [InlineData("MSSql", "C NVARCHAR(MAX)")]
    [InlineData("MySQL", "C LONGTEXT")]
    public void An_undeclared_length_is_still_unbounded(string provider, string expected)
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.String };

        Definition(ConnectorNamed(provider), descriptor).Should().Be(expected);
    }

    /// <summary>
    /// <c>CreateAbstractField</c> falls back from <c>MaxLength</c> to <c>Precision</c> for strings, for
    /// backwards compatibility. The migration path matches that order, so the two producers cannot
    /// disagree about what a string's length is.
    /// </summary>
    [Fact]
    public void A_strings_length_falls_back_to_Precision_exactly_as_the_attribute_path_does()
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.String, Precision = 30 };

        Definition(MSSql(), descriptor).Should().Be("C NVARCHAR(30)");
    }

    // ─────────────────────── Precision/Scale: unfiled, and worse ───────────────────────

    /// <summary>
    /// <b>Not named in the task's criteria, and the more damaging half.</b> Same method, same cause: the
    /// field was not a <c>DecimalField</c>, so a declared precision and scale never reached the column.
    /// </summary>
    [Theory]
    [InlineData("SQLite", "C NUMERIC(18,2)")]
    [InlineData("PostgreSQL", "C NUMERIC(18,2)")]
    [InlineData("MSSql", "C DECIMAL(18,2)")]
    [InlineData("MySQL", "C DECIMAL(18,2)")]
    public void A_declared_precision_and_scale_reach_the_column(string provider, string expected)
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.Decimal, Precision = 18, Scale = 2 };

        Definition(ConnectorNamed(provider), descriptor).Should().Be(expected);
    }

    /// <summary>
    /// ⚠ <b>What a dropped scale actually produced, pinned per provider — the two failure modes differ and
    /// the worse one is on the default provider.</b>
    /// <list type="bullet">
    ///   <item><b>SQLite</b> answers <c>REAL</c> — binary floating point, so a column declared
    ///   <c>DECIMAL(18,2)</c> was stored as a float. SQLite is this framework's default provider.</item>
    ///   <item><b>MSSql</b> and <b>MySQL</b> answer a bare <c>DECIMAL</c>, whose default scale is
    ///   <b>0</b>, so money was truncated to whole units.</item>
    /// </list>
    /// Both were silent. This asserts the provider default is still what an *undeclared* precision gets,
    /// which is correct behaviour and the control for the test above.
    /// </summary>
    [Theory]
    [InlineData("SQLite", "C REAL")]
    [InlineData("PostgreSQL", "C NUMERIC")]
    [InlineData("MSSql", "C DECIMAL")]
    [InlineData("MySQL", "C DECIMAL")]
    public void An_undeclared_precision_still_yields_the_providers_default(string provider, string expected)
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.Decimal };

        Definition(ConnectorNamed(provider), descriptor).Should().Be(expected);
    }

    /// <summary>
    /// Precision without scale is not enough for either producer — <c>ConvertType</c> requires both, so
    /// the factory requires both too rather than inventing a default scale.
    /// </summary>
    [Fact]
    public void Precision_without_scale_does_not_fabricate_a_scale()
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.Decimal, Precision = 18 };

        Definition(MSSql(), descriptor).Should().Be("C DECIMAL");
    }

    // ──────────────────── ColumnName, and the flags that already arrived ────────────────

    /// <summary>
    /// <see cref="FieldDescriptor.ColumnName"/> was silently ignored, so a migration that named its
    /// column got the logical name instead.
    /// </summary>
    [Fact]
    public void A_declared_ColumnName_is_the_emitted_column()
    {
        var descriptor = new FieldDescriptor
        {
            Name = "LogicalName",
            ColumnName = "physical_name",
            Type = FieldType.String,
            MaxLength = 10
        };

        Definition(MSSql(), descriptor).Should().Be("physical_name NVARCHAR(10)");
    }

    /// <summary>A blank <c>ColumnName</c> falls back to <c>Name</c> rather than emitting nothing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_ColumnName_falls_back_to_Name(string? columnName)
    {
        var descriptor = new FieldDescriptor
        {
            Name = "Fallback",
            ColumnName = columnName,
            Type = FieldType.String,
            MaxLength = 10
        };

        Definition(MSSql(), descriptor).Should().Be("Fallback NVARCHAR(10)");
    }

    /// <summary>
    /// The properties that always arrived keep arriving through the new subclasses — whose constructors
    /// take neither <c>notNull</c> nor <c>autoincrement</c>, so those are assigned after construction and
    /// could silently have been dropped by the rewrite.
    /// </summary>
    [Fact]
    public void The_flags_that_already_worked_survive_the_bounded_path()
    {
        var descriptor = new FieldDescriptor
        {
            Name = "C",
            Type = FieldType.String,
            MaxLength = 50,
            IsUnique = true,
            IsRequired = true
        };

        Definition(MSSql(), descriptor).Should().Be("C NVARCHAR(50) UNIQUE NOT NULL");
    }

    /// <summary>
    /// A bounded decimal keeps <c>NOT NULL</c> too — the same assignment risk on the other new subclass.
    /// </summary>
    [Fact]
    public void A_required_bounded_decimal_keeps_its_not_null()
    {
        var descriptor = new FieldDescriptor
        {
            Name = "C",
            Type = FieldType.Decimal,
            Precision = 9,
            Scale = 3,
            IsRequired = true
        };

        Definition(MSSql(), descriptor).Should().Be("C DECIMAL(9,3) NOT NULL");
    }

    // ─────────────── the index consequence, which is why this is rated P1 ───────────────

    /// <summary>
    /// <b>Why the length matters beyond column width.</b> SQL Server cannot use <c>NVARCHAR(MAX)</c> as an
    /// index key at all (Msg 1919) and MySQL cannot index <c>LONGTEXT</c> without a key length
    /// (ERROR 1170). So before TASK-264 a migration that declared <c>maxLength: 50</c> and then an index
    /// over that column could not work on either provider: the column it actually got was unindexable.
    /// </summary>
    /// <remarks>
    /// This is the answer to the task's criterion 2, and it is <i>built</i> rather than <i>refused</i> — a
    /// declared length yields an indexable column. The undeclared case stays loud: <c>CreateIndexes</c>
    /// swallows only "already exists" (MySQL 1061), so Msg 1919 propagates out of an explicit migration
    /// call exactly as TASK-204 says it should. <c>IsIndexed</c> is not set here and cannot be — the
    /// collection and index builders are separate, with no shared state and often separate migrations.
    /// </remarks>
    [Theory]
    [InlineData("MSSql", "C NVARCHAR(50)", "C NVARCHAR(MAX)")]
    [InlineData("MySQL", "C VARCHAR(50)", "C LONGTEXT")]
    public void A_declared_length_is_what_makes_the_column_indexable(string provider, string bounded, string unbounded)
    {
        var connector = ConnectorNamed(provider);

        Definition(connector, new FieldDescriptor { Name = "C", Type = FieldType.String, MaxLength = 50 })
            .Should().Be(bounded, "usable as an index key");
        Definition(connector, new FieldDescriptor { Name = "C", Type = FieldType.String })
            .Should().Be(unbounded, "not usable as an index key on this provider");
    }

    /// <summary>
    /// <c>FieldType.Json</c> maps to <c>DbType.String</c>, so a declared length applies to it as well. The
    /// factory keys on the descriptor's declared type, and leaving Json out would have made a length
    /// silently inert for exactly one field type.
    /// </summary>
    [Fact]
    public void A_json_field_takes_a_declared_length_too()
    {
        var descriptor = new FieldDescriptor { Name = "C", Type = FieldType.Json, MaxLength = 4000 };

        Definition(MSSql(), descriptor).Should().Be("C NVARCHAR(4000)");
    }
}
