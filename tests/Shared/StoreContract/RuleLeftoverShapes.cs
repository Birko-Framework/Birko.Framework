using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Birko.Data.Models;
using Birko.Rules;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>TASK-882's enum: stored as its <c>int</c>.</summary>
public enum RuleLeftoverState
{
    Pending = 0,
    Active = 1,
    Closed = 2,
}

/// <summary>
/// TASK-882's row: non-nullable columns of each kind a rule can compare (int, decimal, DateTime, bool, enum, Guid), the
/// nullable enum and Guid, a nullable string, and <see cref="Num"/> to name each row. <c>DataAnnotations</c>' table
/// attribute, so the shared shapes compile where the SQL attributes do not (<c>Birko.Rules.Tests</c>).
/// </summary>
[Table("T882RuleRows")]
public class RuleLeftoverRow : AbstractModel
{
    public int Num { get; set; }

    public int Amount { get; set; }

    public decimal Price { get; set; }

    public DateTime At { get; set; }

    public bool Flag { get; set; }

    public RuleLeftoverState State { get; set; }

    public RuleLeftoverState? OptState { get; set; }

    public Guid Ref { get; set; }

    public Guid? OptRef { get; set; }

    public string? Name { get; set; }
}

/// <summary>One rule shape and what the equivalent C# lambda over the entity answers.</summary>
public sealed class RuleLeftoverShape
{
    private RuleLeftoverShape(string item, Func<Rule> rule, Func<RuleLeftoverRow, bool>? lambda, bool refused)
    {
        Item = item;
        Rule = rule;
        Lambda = lambda;
        Refused = refused;
    }

    /// <summary>The TASK-882 item: 1 (null against a non-nullable column), 2 (Like), 3 (values in their stored form).</summary>
    public string Item { get; }

    public Func<Rule> Rule { get; }

    /// <summary>The C# lambda; null when the rule's value has no C# form (an unconvertible value): it matches nothing.</summary>
    public Func<RuleLeftoverRow, bool>? Lambda { get; }

    /// <summary>The shape has no form in every runner, so every runner refuses it by name.</summary>
    public bool Refused { get; }

    public static RuleLeftoverShape Answer(string item, Func<Rule> rule, Func<RuleLeftoverRow, bool> lambda) => new(item, rule, lambda, false);

    /// <summary>A value with no C# form for the member: matches nothing, plain or negated (TASK-116's invariant).</summary>
    public static RuleLeftoverShape Nothing(string item, Func<Rule> rule) => new(item, rule, null, false);

    public static RuleLeftoverShape Refusal(string item, Func<Rule> rule) => new(item, rule, null, true);
}

/// <summary>
/// TASK-882 — rule shapes the three runners (<see cref="RuleEvaluator"/>, the rule compiled by
/// <see cref="RuleExpressionConverter"/>, and the SQL rule converter) answered differently from each other or from C#:
/// a null value against a non-nullable column, <c>Like</c> (an interior <c>%</c>, and case), and values given in another
/// form than the member's (an enum or a Guid as text). Each shape carries the C# lambda it means.
/// <para>Shared source: linked into <c>Birko.Rules.Tests</c> (the in-memory runners) and each SQL provider's tests.</para>
/// </summary>
public static class RuleLeftoverShapes
{
    public static readonly Guid G1 = new("1b4e28ba-2fa1-11d2-883f-0016d3cca427");
    public static readonly Guid G2 = new("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    public static readonly Guid G3 = new("a3bb189e-8bf9-3888-9912-ace4e6543002");
    public static readonly DateTime D1 = new(2026, 1, 1, 0, 0, 0);
    public static readonly DateTime D2 = new(2026, 1, 2, 3, 4, 5);
    public static readonly DateTime D3 = new(2026, 3, 1, 12, 0, 0);

    // Typed nulls, so the lambdas below are C#'s lifted comparisons and not constant-folded warnings.
    private static readonly int? NoInt = null;
    private static readonly decimal? NoDecimal = null;
    private static readonly DateTime? NoDate = null;
    private static readonly bool? NoBool = null;
    private static readonly RuleLeftoverState? NoState = null;
    private static readonly Guid? NoGuid = null;

    private static readonly string?[] Names =
    {
        "abc", "ABC", "Abc", "aXb", "a%b", "axyb", "café", "CAFÉ", "cafe", "ı", "I", "i", null, "a_c", "abc ", "AB", "a%%",
    };

    public static RuleLeftoverRow[] Rows() => Names.Select((name, i) => new RuleLeftoverRow
    {
        Guid = Guid.NewGuid(),
        Num = i + 1,
        Amount = i % 7,
        Price = new[] { 1.5m, 2.25m, 10m }[i % 3],
        At = new[] { D1, D2, D3 }[i % 3],
        Flag = i % 2 == 0,
        State = (RuleLeftoverState)(i % 3),
        OptState = i % 4 == 0 ? null : (RuleLeftoverState)((i + 1) % 3),
        Ref = new[] { G1, G2, G3 }[i % 3],
        OptRef = i % 4 == 1 ? null : new[] { G2, G3, G1 }[i % 3],
        Name = name,
    }).ToArray();

    private static Rule R(string field, ComparisonOperator op, object? value) => new(field, op, value);

    private static Rule Between(string field, object? lower, object? upper) => new(field, ComparisonOperator.Between, lower) { UpperValue = upper };

    private static bool OIC(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private const StringComparison Ci = StringComparison.OrdinalIgnoreCase;

    public static readonly IReadOnlyDictionary<string, RuleLeftoverShape> Shapes = new Dictionary<string, RuleLeftoverShape>
    {
        // ── 1. a null value against a non-nullable column: C#'s lifted comparison ──
        ["Amount Equal null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.Equal, null), x => (int?)x.Amount == NoInt),
        ["Amount NotEqual null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.NotEqual, null), x => (int?)x.Amount != NoInt),
        ["Amount GreaterThan null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.GreaterThan, null), x => (int?)x.Amount > NoInt),
        ["Amount GreaterThanOrEqual null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.GreaterThanOrEqual, null), x => (int?)x.Amount >= NoInt),
        ["Amount LessThan null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.LessThan, null), x => (int?)x.Amount < NoInt),
        ["Amount LessThanOrEqual null"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.LessThanOrEqual, null), x => (int?)x.Amount <= NoInt),
        ["Amount Between null..5"] = RuleLeftoverShape.Answer("1", () => Between("Amount", null, 5), x => (int?)x.Amount >= NoInt && x.Amount <= 5),
        ["Amount Between 3..null"] = RuleLeftoverShape.Answer("1", () => Between("Amount", 3, null), x => x.Amount >= 3 && (int?)x.Amount <= NoInt),
        ["Amount In [null,5]"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.In, new object?[] { null, 5 }), x => (int?)x.Amount == NoInt || x.Amount == 5),
        ["Amount NotIn [null,5]"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.NotIn, new object?[] { null, 5 }), x => !((int?)x.Amount == NoInt || x.Amount == 5)),
        ["Amount In [null]"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.In, new object?[] { null }), x => (int?)x.Amount == NoInt),
        ["Amount NotIn [null]"] = RuleLeftoverShape.Answer("1", () => R("Amount", ComparisonOperator.NotIn, new object?[] { null }), x => (int?)x.Amount != NoInt),
        ["Price Equal null"] = RuleLeftoverShape.Answer("1", () => R("Price", ComparisonOperator.Equal, null), x => (decimal?)x.Price == NoDecimal),
        ["Price LessThan null"] = RuleLeftoverShape.Answer("1", () => R("Price", ComparisonOperator.LessThan, null), x => (decimal?)x.Price < NoDecimal),
        ["At NotEqual null"] = RuleLeftoverShape.Answer("1", () => R("At", ComparisonOperator.NotEqual, null), x => (DateTime?)x.At != NoDate),
        ["At GreaterThan null"] = RuleLeftoverShape.Answer("1", () => R("At", ComparisonOperator.GreaterThan, null), x => (DateTime?)x.At > NoDate),
        ["Flag Equal null"] = RuleLeftoverShape.Answer("1", () => R("Flag", ComparisonOperator.Equal, null), x => (bool?)x.Flag == NoBool),
        ["Flag NotEqual null"] = RuleLeftoverShape.Answer("1", () => R("Flag", ComparisonOperator.NotEqual, null), x => (bool?)x.Flag != NoBool),
        ["State Equal null"] = RuleLeftoverShape.Answer("1", () => R("State", ComparisonOperator.Equal, null), x => (RuleLeftoverState?)x.State == NoState),
        ["State NotEqual null"] = RuleLeftoverShape.Answer("1", () => R("State", ComparisonOperator.NotEqual, null), x => (RuleLeftoverState?)x.State != NoState),
        ["Ref Equal null"] = RuleLeftoverShape.Answer("1", () => R("Ref", ComparisonOperator.Equal, null), x => (Guid?)x.Ref == NoGuid),
        ["Ref NotEqual null"] = RuleLeftoverShape.Answer("1", () => R("Ref", ComparisonOperator.NotEqual, null), x => (Guid?)x.Ref != NoGuid),
        ["Ref In [null,G1]"] = RuleLeftoverShape.Answer("1", () => R("Ref", ComparisonOperator.In, new object?[] { null, G1 }), x => (Guid?)x.Ref == NoGuid || x.Ref == G1),

        // ── 2. Like: an interior % has no form anywhere; the rest is OrdinalIgnoreCase, as Contains/StartsWith/EndsWith ──
        ["Name Like a%b"] = RuleLeftoverShape.Refusal("2", () => R("Name", ComparisonOperator.Like, "a%b")),
        ["Name Like %a%b%"] = RuleLeftoverShape.Refusal("2", () => R("Name", ComparisonOperator.Like, "%a%b%")),
        ["Name Like a%b%"] = RuleLeftoverShape.Refusal("2", () => R("Name", ComparisonOperator.Like, "a%b%")),
        ["Name Like %a%b"] = RuleLeftoverShape.Refusal("2", () => R("Name", ComparisonOperator.Like, "%a%b")),
        ["Name Like ABC"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "ABC"), x => OIC(x.Name, "ABC")),
        ["Name Like CAFÉ"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "CAFÉ"), x => OIC(x.Name, "CAFÉ")),
        ["Name Like I"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "I"), x => OIC(x.Name, "I")),
        ["Name Like a_c"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "a_c"), x => OIC(x.Name, "a_c")),
        ["Name Like %B%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "%B%"), x => x.Name != null && x.Name.Contains("B", Ci)),
        ["Name Like AB%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "AB%"), x => x.Name != null && x.Name.StartsWith("AB", Ci)),
        ["Name Like %BC"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "%BC"), x => x.Name != null && x.Name.EndsWith("BC", Ci)),
        ["Name Like %_%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "%_%"), x => x.Name != null && x.Name.Contains("_", Ci)),
        ["Name Like a%%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "a%%"), x => x.Name != null && x.Name.StartsWith("a", Ci)),
        ["Name Like %%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "%%"), x => x.Name != null),
        ["Name Like %É%"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Like, "%É%"), x => x.Name != null && x.Name.Contains("É", Ci)),
        ["Name Contains B"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Contains, "B"), x => x.Name != null && x.Name.Contains("B", Ci)),
        ["Name NotContains B"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.NotContains, "B"), x => !(x.Name != null && x.Name.Contains("B", Ci))),
        ["Name StartsWith AB"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.StartsWith, "AB"), x => x.Name != null && x.Name.StartsWith("AB", Ci)),
        ["Name EndsWith BC"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.EndsWith, "BC"), x => x.Name != null && x.Name.EndsWith("BC", Ci)),
        ["Name Contains i"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Contains, "i"), x => x.Name != null && x.Name.Contains("i", Ci)),
        ["Name Contains %"] = RuleLeftoverShape.Answer("2", () => R("Name", ComparisonOperator.Contains, "%"), x => x.Name != null && x.Name.Contains("%", Ci)),
        ["Amount Contains 1"] = RuleLeftoverShape.Nothing("2", () => R("Amount", ComparisonOperator.Contains, "1")),
        ["Amount Like %1%"] = RuleLeftoverShape.Nothing("2", () => R("Amount", ComparisonOperator.Like, "%1%")),

        // ── 3. a value in another form than the member's: converted as the compiled rule converts it ──
        ["State Equal \"Active\""] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.Equal, "Active"), x => x.State == RuleLeftoverState.Active),
        ["State Equal \"active\""] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.Equal, "active"), x => x.State == RuleLeftoverState.Active),
        ["State Equal 1"] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.Equal, 1), x => x.State == RuleLeftoverState.Active),
        ["State Equal enum"] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.Equal, RuleLeftoverState.Closed), x => x.State == RuleLeftoverState.Closed),
        ["State NotEqual \"Active\""] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.NotEqual, "Active"), x => x.State != RuleLeftoverState.Active),
        ["State In [\"Active\",\"Closed\"]"] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.In, new object?[] { "Active", "Closed" }), x => x.State == RuleLeftoverState.Active || x.State == RuleLeftoverState.Closed),
        ["State NotIn [\"Active\"]"] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.NotIn, new object?[] { "Active" }), x => x.State != RuleLeftoverState.Active),
        ["State GreaterThan \"Pending\""] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.GreaterThan, "Pending"), x => x.State > RuleLeftoverState.Pending),
        ["State LessThan enum"] = RuleLeftoverShape.Answer("3", () => R("State", ComparisonOperator.LessThan, RuleLeftoverState.Closed), x => x.State < RuleLeftoverState.Closed),
        ["State Between \"Pending\"..\"Active\""] = RuleLeftoverShape.Answer("3", () => Between("State", "Pending", "Active"), x => x.State >= RuleLeftoverState.Pending && x.State <= RuleLeftoverState.Active),
        ["OptState Equal \"Active\""] = RuleLeftoverShape.Answer("3", () => R("OptState", ComparisonOperator.Equal, "Active"), x => x.OptState == RuleLeftoverState.Active),
        ["OptState NotEqual \"Closed\""] = RuleLeftoverShape.Answer("3", () => R("OptState", ComparisonOperator.NotEqual, "Closed"), x => x.OptState != RuleLeftoverState.Closed),
        ["State Equal \"Bogus\""] = RuleLeftoverShape.Nothing("3", () => R("State", ComparisonOperator.Equal, "Bogus")),
        ["State In [\"Active\",\"Bogus\"]"] = RuleLeftoverShape.Nothing("3", () => R("State", ComparisonOperator.In, new object?[] { "Active", "Bogus" })),
        ["Ref Equal text"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.Equal, G1.ToString()), x => x.Ref == G1),
        ["Ref Equal TEXT"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.Equal, G1.ToString().ToUpperInvariant()), x => x.Ref == G1),
        ["Ref Equal text N"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.Equal, G2.ToString("N")), x => x.Ref == G2),
        ["Ref NotEqual text"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.NotEqual, G1.ToString()), x => x.Ref != G1),
        ["Ref In [text,text]"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.In, new object?[] { G1.ToString(), G3.ToString().ToUpperInvariant() }), x => x.Ref == G1 || x.Ref == G3),
        ["Ref Equal guid"] = RuleLeftoverShape.Answer("3", () => R("Ref", ComparisonOperator.Equal, G2), x => x.Ref == G2),
        ["OptRef Equal text"] = RuleLeftoverShape.Answer("3", () => R("OptRef", ComparisonOperator.Equal, G2.ToString()), x => x.OptRef == G2),
        ["OptRef NotIn [text]"] = RuleLeftoverShape.Answer("3", () => R("OptRef", ComparisonOperator.NotIn, new object?[] { G3.ToString() }), x => x.OptRef != G3),
        ["Ref Equal \"not-a-guid\""] = RuleLeftoverShape.Nothing("3", () => R("Ref", ComparisonOperator.Equal, "not-a-guid")),
        ["At Equal text"] = RuleLeftoverShape.Answer("3", () => R("At", ComparisonOperator.Equal, "2026-01-02T03:04:05"), x => x.At == D2),
        ["At GreaterThan text"] = RuleLeftoverShape.Answer("3", () => R("At", ComparisonOperator.GreaterThan, "2026-01-02"), x => x.At > new DateTime(2026, 1, 2)),
        ["At Between text..text"] = RuleLeftoverShape.Answer("3", () => Between("At", "2026-01-01", "2026-01-31"), x => x.At >= D1 && x.At <= new DateTime(2026, 1, 31)),
        ["Amount Equal \"5\""] = RuleLeftoverShape.Answer("3", () => R("Amount", ComparisonOperator.Equal, "5"), x => x.Amount == 5),
        ["Amount Equal \"abc\""] = RuleLeftoverShape.Nothing("3", () => R("Amount", ComparisonOperator.Equal, "abc")),
        ["Price GreaterThan \"2\""] = RuleLeftoverShape.Answer("3", () => R("Price", ComparisonOperator.GreaterThan, "2"), x => x.Price > 2m),
        ["Amount GreaterThan 2.5"] = RuleLeftoverShape.Answer("3", () => R("Amount", ComparisonOperator.GreaterThan, 2.5), x => x.Amount > 2.5),
        ["Flag Equal \"true\""] = RuleLeftoverShape.Answer("3", () => R("Flag", ComparisonOperator.Equal, "true"), x => x.Flag),
    };

    public static readonly string[] Forms = { "plain", "negated", "NOT group" };

    /// <summary>Each shape plain, negated (<see cref="Rule.IsNegated"/>) and inside a negated AND group.</summary>
    public static IEnumerable<object[]> Cases(string? item = null)
    {
        foreach (var (name, shape) in Shapes)
        {
            if (item != null && shape.Item != item) continue;
            foreach (var form in Forms)
                yield return new object[] { name, form };
        }
    }

    public static IRule Build(string shape, string form)
    {
        var rule = Shapes[shape].Rule();
        switch (form)
        {
            case "negated":
                rule.IsNegated = true;
                return rule;
            case "NOT group":
                // NOT (rule AND Num > 0): by De Morgan NOT rule OR Num <= 0, and every Num is positive.
                return new RuleGroup(LogicOperator.And, rule, R("Num", ComparisonOperator.GreaterThan, 0)) { IsNegated = true };
            default:
                return rule;
        }
    }

    /// <summary>The rows C# selects for the shape in <paramref name="form"/>; null when the shape is refused.</summary>
    public static int[]? Expected(string shape, string form)
    {
        var s = Shapes[shape];
        if (s.Refused) return null;
        if (s.Lambda == null) return Array.Empty<int>();
        var lambda = s.Lambda;
        Func<RuleLeftoverRow, bool> predicate = form == "plain" ? lambda : x => !lambda(x);
        return Rows().Where(predicate).Select(r => r.Num).OrderBy(n => n).ToArray();
    }

    public static int[] Nums(IEnumerable<RuleLeftoverRow> rows) => rows.Select(r => r.Num).OrderBy(n => n).ToArray();
}
