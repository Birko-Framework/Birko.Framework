using System;
using System.Data.Common;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-253 — the one producer for a name that reaches SQL inside a string <b>literal</b> rather than as an
/// identifier.
///
/// <para><b>Why this needs its own producer.</b> Everywhere else this framework emits an identifier the parser
/// folds it, so the bare/quoted convention (§ Conventions, TASK-209/211) settles the question. A handful of
/// statements instead take a name as a quoted literal — <c>create_hypertable('"T"', 'ts')</c>,
/// <c>add_compression_policy</c>, <c>OBJECT_ID('T')</c>, every <c>sys.indexes WHERE name = '…'</c> probe — and
/// there the parser never sees an identifier at all. Neither half of the convention applies, and the two
/// arguments of one function can need <b>opposite</b> treatments: TASK-472 measured
/// <c>create_hypertable</c> failing on a bare table (<c>42P01</c>, swallowed as a missing table, so <b>no
/// hypertable existed for any PascalCase entity</b>) and on an unfolded column (<c>42703</c>, loud).</para>
///
/// <para><b>What these tests are for.</b> Two things a reader would otherwise have to take on trust: that
/// <see cref="AbstractConnectorBase.RegclassLiteral"/> composes the two escapes in the order that cannot
/// collide, and that <see cref="AbstractConnectorBase.FoldsUnquotedIdentifiers"/> is <i>load-bearing rather
/// than decorative</i> — every sink TASK-253 wires reads it as <c>true</c> (only the PostgreSQL family emits
/// these statements), so without the <c>false</c> case below the flag would never be exercised in both
/// directions and could be deleted with no test noticing.</para>
/// </summary>
public class LiteralIdentifierProducerTests
{
    /// <summary>Default base behaviour: ANSI double quotes, no folding — i.e. SQLite / MySQL / MSSql.</summary>
    private sealed class NonFoldingConnector : AbstractConnectorBase
    {
        public NonFoldingConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    /// <summary>Stands in for PostgreSQL / TimescaleDB, which fold an unquoted identifier to lower case.</summary>
    private sealed class FoldingConnector : AbstractConnectorBase
    {
        public FoldingConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override bool FoldsUnquotedIdentifiers => true;

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Quotes with backticks, as <c>MySQLConnector</c> does. Present only to prove
    /// <see cref="AbstractConnectorBase.RegclassLiteral"/> delegates to
    /// <see cref="AbstractConnectorBase.QuoteIdentifier"/> instead of hardcoding the ANSI quote.
    /// </summary>
    private sealed class BacktickConnector : AbstractConnectorBase
    {
        public BacktickConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override string QuoteIdentifier(string identifier)
            => "`" + identifier.Replace("`", "``") + "`";

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    // ── SqlLiteral.EscapeLiteral — the ANSI half ──

    [Fact]
    public void EscapeLiteral_DoublesSingleQuotes()
        => SqlLiteral.EscapeLiteral("me'tric").Should().Be("me''tric");

    /// <summary>
    /// It escapes and nothing else. Adding the surrounding quotes here would make the nesting in
    /// <see cref="AbstractConnectorBase.RegclassLiteral"/> impossible to express.
    /// </summary>
    [Fact]
    public void EscapeLiteral_DoesNotAddSurroundingQuotes()
    {
        var escaped = SqlLiteral.EscapeLiteral("metrics");

        escaped.Should().Be("metrics");
        escaped.Should().NotStartWith("'");
        escaped.Should().NotEndWith("'");
    }

    /// <summary>
    /// <b>Refuses null rather than escaping it to empty — reversed from the first version of this helper, and
    /// the convergence is what exposed why.</b> Returning <see cref="string.Empty"/> looked accommodating,
    /// but once the framework's 18 hand-rolled copies were converged onto it (step 7), every one of those
    /// sinks would have turned a null identifier into an <i>empty</i> one — a silently malformed statement,
    /// where the hand-written <c>Replace</c> threw. Loud beats quiet (§ SH-H037), and the message names where
    /// to handle it.
    /// </summary>
    [Fact]
    public void EscapeLiteral_RefusesNull()
    {
        var act = () => SqlLiteral.EscapeLiteral(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithMessage("*empty identifier*", "the refusal has to say what the quiet alternative would emit");
    }

    // ── RegclassLiteral — quote as an identifier, THEN escape for the literal ──

    /// <summary>
    /// The load-bearing case. A PascalCase name must come back carrying its own identifier quotes, because the
    /// regclass argument is re-parsed as an identifier after the literal is unwrapped — bare, it folds and
    /// resolves to a relation the framework never created.
    /// </summary>
    [Fact]
    public void RegclassLiteral_QuotesThePascalCaseName()
        => new NonFoldingConnector().RegclassLiteral("Widgets").Should().Be("\"Widgets\"");

    /// <summary>
    /// A regclass is <b>never</b> folded, on either kind of provider: the quotes it carries are exactly what
    /// suppresses folding once the literal is unwrapped. This is the asymmetry against
    /// <see cref="CatalogueNameLiteral_FoldsOnAFoldingProvider"/>, and getting it backwards is the defect.
    /// </summary>
    [Fact]
    public void RegclassLiteral_DoesNotFoldEvenOnAFoldingProvider()
        => new FoldingConnector().RegclassLiteral("Widgets").Should().Be("\"Widgets\"");

    /// <summary>
    /// Order matters and only one order is safe: quote first (which doubles <c>"</c>), then escape (which
    /// doubles <c>'</c>). Neither step can introduce the other's metacharacter, so a name carrying both comes
    /// out with both doubled and nothing re-escaped.
    /// </summary>
    [Fact]
    public void RegclassLiteral_EscapesBothQuoteKinds()
        => new NonFoldingConnector().RegclassLiteral("we\"ir'd").Should().Be("\"we\"\"ir''d\"");

    /// <summary>
    /// Delegates to the provider's own <see cref="AbstractConnectorBase.QuoteIdentifier"/> rather than assuming
    /// ANSI — otherwise this producer would silently emit the wrong quote on MySQL and MSSql.
    /// </summary>
    [Fact]
    public void RegclassLiteral_UsesTheProvidersOwnQuoteCharacter()
        => new BacktickConnector().RegclassLiteral("Widgets").Should().Be("`Widgets`");

    // ── CatalogueNameLiteral — pre-fold, and never quote ──

    /// <summary>
    /// Compared literally against a catalogue column, so it must arrive in the form the catalogue stores.
    /// Column definitions are emitted bare (TASK-209), so on a folding provider that is lower case.
    /// </summary>
    [Fact]
    public void CatalogueNameLiteral_FoldsOnAFoldingProvider()
        => new FoldingConnector().CatalogueNameLiteral("Ts").Should().Be("ts");

    /// <summary>
    /// <b>The <c>false</c> side of the flag, and the reason it is a capability rather than a constant.</b>
    /// Every sink TASK-253 wires runs on PostgreSQL, where this reads <c>true</c>; without this test the
    /// non-folding branch would be unexercised and the flag indistinguishable from an unconditional
    /// <c>ToLowerInvariant()</c>. A provider that preserves the spelling it was given must have the spelling
    /// preserved.
    /// </summary>
    [Fact]
    public void CatalogueNameLiteral_PreservesCaseOnANonFoldingProvider()
        => new NonFoldingConnector().CatalogueNameLiteral("Ts").Should().Be("Ts");

    /// <summary>
    /// Quoting here would be actively wrong, not merely redundant: the comparison is textual, so a
    /// <c>"Ts"</c> would be looked up with its quotes included and match nothing.
    /// </summary>
    [Fact]
    public void CatalogueNameLiteral_AddsNoQuotes()
    {
        var rendered = new FoldingConnector().CatalogueNameLiteral("Ts");

        rendered.Should().NotContain("\"");
        rendered.Should().NotContain("'");
    }

    /// <summary>Still escapes for the literal it is going into, folding or not.</summary>
    [Fact]
    public void CatalogueNameLiteral_EscapesSingleQuotes()
        => new FoldingConnector().CatalogueNameLiteral("T's").Should().Be("t''s");

    // ── the capability's default ──

    /// <summary>
    /// Defaults to <c>false</c>, so a provider that does not fold needs no override and a new connector cannot
    /// inherit PostgreSQL's behaviour by accident.
    /// </summary>
    [Fact]
    public void FoldsUnquotedIdentifiers_DefaultsToFalse()
        => new NonFoldingConnector().FoldsUnquotedIdentifiers.Should().BeFalse();
}
