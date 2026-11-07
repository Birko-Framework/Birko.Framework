using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.Connectors;

/// <summary>
/// TASK-833 (FIELD-051, Symbio): the planner that splits a statement whose <c>IN</c> list is past the provider's parameter
/// limit, or past the chunk size. Pure: the probe is a function that counts the parameters a condition list would bind.
/// </summary>
public class InListChunkPlannerTests
{
    private const int Max = 40;
    private const int Chunk = 10;

    private static Condition In(string name, IEnumerable<object?> values, bool isNot = false, bool isOr = false)
        => new(name, values.ToArray(), ConditionType.In, isNot: isNot, isOr: isOr);

    private static Condition Eq(string name, object value, bool isOr = false)
        => new(name, new[] { value }, ConditionType.Equal, isOr: isOr);

    private static Condition Group(bool isOr, bool isNot, params Condition[] children)
        => new(null, null, ConditionType.Equal, isNot: isNot, isOr: isOr, subConditions: children);

    private static List<object?> Guids(int n) => Enumerable.Range(0, n).Select(_ => (object?)Guid.NewGuid()).ToList();

    /// <summary>What a renderer binds: every IN value, one per other leaf, plus <paramref name="extra"/> (SET, limit).</summary>
    private static Func<IReadOnlyList<Condition>, int> Probe(int extra = 0, List<IReadOnlyList<Condition>>? seen = null)
        => conditions =>
        {
            seen?.Add(conditions);
            return conditions.Sum(Params) + extra;
        };

    private static int Params(Condition c)
    {
        if (c.SubConditions?.Any() == true) return c.SubConditions.Sum(Params);
        if (c.Type == ConditionType.In) return c.IsField ? 0 : c.Values!.Cast<object?>().Count();
        return 1;
    }

    private static InListChunkPlan Plan(IReadOnlyList<Condition> conditions, int extra = 0, string? statementReason = null, string operation = "read")
        => InListChunkPlanner.Plan(operation, conditions, Max, Chunk, Probe(extra), statementReason);

    /// <summary>The values of the IN on <paramref name="name"/> in each chunk, wherever it sits in the tree.</summary>
    private static List<List<object?>> ChunkValues(InListChunkPlan plan, string name)
        => plan.Chunks!.Select(chunk => FindIn(chunk, name)!.Values!.Cast<object?>().ToList()).ToList();

    private static Condition? FindIn(IEnumerable<Condition> conditions, string name)
    {
        foreach (var c in conditions)
        {
            if (c.SubConditions?.Any() == true)
            {
                var inner = FindIn(c.SubConditions, name);
                if (inner != null) return inner;
                continue;
            }
            if (c.Type == ConditionType.In && c.Name == name) return c;
        }
        return null;
    }

    // ── splits ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_list_under_and_is_split_into_chunks_that_cover_it_exactly_once()
    {
        var ids = Guids(35);
        var count = Eq("W.Count", 3);
        var plan = Plan(new[] { In("W.Guid", ids), count });

        plan.IsChunked.Should().BeTrue();
        plan.ChunkSize.Should().Be(Chunk);
        var chunks = ChunkValues(plan, "W.Guid");
        chunks.Select(c => c.Count).Should().Equal(10, 10, 10, 5);
        chunks.SelectMany(c => c).Should().Equal(ids);
        plan.Chunks!.Should().AllSatisfy(chunk => chunk[1].Should().BeSameAs(count));
    }

    [Fact]
    public void A_list_inside_nested_and_groups_is_split_with_the_groups_around_it()
    {
        // The shape the tenant filter (ModelByTenant) and the soft-delete filter give: nested AND groups.
        var ids = Guids(25);
        var tenant = Eq("W.Tenant", Guid.NewGuid());
        var deleted = new Condition("W.DeletedAt", null, ConditionType.IsNull);
        var conditions = new[] { Group(false, false, Group(false, false, In("W.Guid", ids), deleted), tenant) };

        var plan = Plan(conditions);

        plan.IsChunked.Should().BeTrue();
        ChunkValues(plan, "W.Guid").SelectMany(c => c).Should().Equal(ids);
        foreach (var chunk in plan.Chunks!)
        {
            var outer = chunk.Single().SubConditions!.ToList();
            outer[1].Should().BeSameAs(tenant, "a node off the path is the caller's own, shared");
            outer[0].SubConditions!.ToList()[1].Should().BeSameAs(deleted);
        }
    }

    [Fact]
    public void Duplicates_are_dropped_so_no_row_can_match_two_chunks()
    {
        var ids = Guids(15);
        var withDuplicates = ids.Concat(ids).Concat(ids).ToList();   // 45 values, 15 distinct

        var plan = Plan(new[] { In("W.Guid", withDuplicates) });

        var chunks = ChunkValues(plan, "W.Guid");
        chunks.SelectMany(c => c).Should().Equal(ids);
        chunks.Should().AllSatisfy(c => c.Should().NotBeEmpty());
    }

    [Fact]
    public void No_chunk_is_empty()
    {
        var plan = Plan(new[] { In("W.Count", Enumerable.Range(0, 20).Select(i => (object?)i)) });

        ChunkValues(plan, "W.Count").Select(c => c.Count).Should().Equal(10, 10);
    }

    [Fact]
    public void The_caller_s_tree_is_never_written_to()
    {
        var ids = Guids(25);
        var leaf = In("W.Guid", ids);
        var group = Group(false, false, leaf, Eq("W.Count", 1));
        var conditions = new List<Condition> { group };
        var before = leaf.Values;

        var plan = Plan(conditions);

        plan.IsChunked.Should().BeTrue();
        conditions.Single().Should().BeSameAs(group);
        group.SubConditions!.First().Should().BeSameAs(leaf);
        leaf.Values.Should().BeSameAs(before);
        ((object?[])leaf.Values!).Should().Equal(ids);
        plan.Chunks!.Should().AllSatisfy(chunk => chunk.Single().Should().NotBeSameAs(group));
    }

    [Fact]
    public void The_largest_list_that_can_be_split_is_the_one_split()
    {
        var small = Guids(12);
        var large = Guids(30);

        var plan = InListChunkPlanner.Plan("read", new[] { In("W.A", small), In("W.B", large) }, 100, Chunk, Probe());

        plan.IsChunked.Should().BeTrue();
        ChunkValues(plan, "W.B").SelectMany(c => c).Should().Equal(large);
        plan.Chunks!.Should().AllSatisfy(chunk => FindIn(chunk, "W.A")!.Values.Should().BeEquivalentTo(small));
    }

    [Fact]
    public void The_chunk_size_shrinks_to_leave_room_for_the_other_parameters()
    {
        // 35 other parameters under a maximum of 40 leave room for 5 values per statement.
        var plan = Plan(new[] { In("W.Guid", Guids(12)) }, extra: 35);

        plan.ChunkSize.Should().Be(5);
        ChunkValues(plan, "W.Guid").Select(c => c.Count).Should().Equal(5, 5, 2);
    }

    [Fact]
    public void The_probe_sees_the_list_cut_to_one_value_and_runs_once()
    {
        var seen = new List<IReadOnlyList<Condition>>();
        InListChunkPlanner.Plan("read", new[] { In("W.Guid", Guids(25)), Eq("W.Count", 1) }, Max, Chunk, Probe(seen: seen));

        seen.Should().ContainSingle();
        FindIn(seen[0], "W.Guid")!.Values!.Cast<object?>().Should().ContainSingle();
    }

    // ── nothing changes ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_list_within_the_chunk_size_is_unchanged_and_builds_no_probe()
    {
        var seen = new List<IReadOnlyList<Condition>>();
        var plan = InListChunkPlanner.Plan("read", new[] { In("W.Guid", Guids(Chunk)) }, Max, Chunk, Probe(seen: seen));

        plan.Should().BeSameAs(InListChunkPlan.Unchanged);
        seen.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_in_is_never_chunked()
    {
        Plan(new[] { In("W.Guid", Array.Empty<object?>()) }).IsChunked.Should().BeFalse();
    }

    [Fact]
    public void A_column_to_column_in_binds_nothing_and_is_not_counted()
    {
        var field = new Condition("W.A", Enumerable.Range(0, 50).Select(i => (object)("W.C" + i)).ToArray(), ConditionType.In, isField: true);

        Plan(new[] { field }).Should().BeSameAs(InListChunkPlan.Unchanged);
    }

    [Theory]
    [InlineData("or")]
    [InlineData("not in")]
    [InlineData("string")]
    [InlineData("sort")]
    public void A_shape_that_cannot_be_split_stays_one_statement_while_within_the_maximum(string shape)
    {
        var plan = shape switch
        {
            "or" => Plan(new[] { Eq("W.Count", 1), In("W.Guid", Guids(30), isOr: true) }),
            "not in" => Plan(new[] { In("W.Guid", Guids(30), isNot: true) }),
            "string" => Plan(new[] { In("W.Name", Enumerable.Range(0, 30).Select(i => (object?)("n" + i))) }),
            _ => Plan(new[] { In("W.Guid", Guids(30)) }, statementReason: "the read is sorted"),
        };

        plan.Should().BeSameAs(InListChunkPlan.Unchanged, "nothing that works today starts refusing");
    }

    // ── refusals past the maximum ────────────────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> Unsplittable()
    {
        yield return new object[] { "a root OR", new[] { Eq("W.Count", 1), In("W.Guid", Guids(50), isOr: true) }, "OR" };
        yield return new object[] { "an OR group", new[] { Group(true, false, In("W.Guid", Guids(50)), Eq("W.Count", 1)) }, "OR group" };
        yield return new object[] { "a negated group", new[] { Group(false, true, In("W.Guid", Guids(50)), Eq("W.Count", 1)) }, "negated group" };
        yield return new object[] { "a NOT IN", new[] { In("W.Guid", Guids(50), isNot: true) }, "NOT IN" };
        yield return new object[] { "a string list", new[] { In("W.Name", Enumerable.Range(0, 50).Select(i => (object?)("n" + i))) }, "strings" };
        yield return new object[] { "an AND inside an OR", new[] { Group(true, false, Eq("W.Count", 1), Group(false, false, In("W.Guid", Guids(50)), Eq("W.Count", 2))) }, "OR group" };
    }

    [Theory]
    [MemberData(nameof(Unsplittable))]
    public void A_list_past_the_maximum_that_cannot_be_split_is_refused_with_its_reason(string label, Condition[] conditions, string reason)
    {
        var plan = Plan(conditions, operation: "delete");

        plan.IsRefused.Should().BeTrue(label);
        plan.Refusal.Should().Contain(reason).And.Contain("delete").And.Contain("40").And.Contain("TASK-833");
        var act = () => plan.ThrowIfRefused();
        act.Should().Throw<NotSupportedException>().Which.Message.Should().Be(plan.Refusal);
    }

    [Theory]
    [InlineData("the read is sorted")]
    [InlineData("the read is paged (limit / offset)")]
    [InlineData("it is an aggregate")]
    public void A_statement_reason_refuses_a_splittable_list_past_the_maximum(string reason)
    {
        var plan = Plan(new[] { In("W.Guid", Guids(50)) }, statementReason: reason);

        plan.Refusal.Should().Contain(reason);
    }

    [Fact]
    public void The_refusal_names_the_count_and_the_column_and_never_a_value()
    {
        var names = Enumerable.Range(0, 45).Select(i => (object?)("secret-" + i)).ToList();
        var plan = Plan(new[] { In("W.Name", names), Eq("W.Count", 7) });

        plan.Refusal.Should().Contain("46").And.Contain("W.Name").And.NotContain("secret");
    }

    [Fact]
    public void A_list_mixing_value_types_is_refused_past_the_maximum()
    {
        var mixed = Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? (object?)i : (long)i);

        Plan(new[] { In("W.Count", mixed) }).Refusal.Should().Contain("mix types");
    }

    [Fact]
    public void One_instant_at_two_offsets_is_refused_past_the_maximum()
    {
        var at = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var values = Enumerable.Range(0, 48).Select(i => (object?)at.AddMinutes(i))
            .Append(at.ToOffset(TimeSpan.FromHours(2)));

        Plan(new[] { In("W.At", values) }).Refusal.Should().Contain("two offsets");
    }

    [Fact]
    public void A_date_list_with_sub_microsecond_ticks_is_refused_past_the_maximum_and_kept_whole_within_it()
    {
        // Two values 100 ns apart are one stored value on PostgreSQL timestamp / MySQL DATETIME(6) (drivers round).
        var at = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var values = Enumerable.Range(0, 50).Select(i => (object?)at.AddTicks(i * 10)).Append(at.AddTicks(1)).ToList();

        Plan(new[] { In("W.At", values) }).Refusal.Should().Contain("sub-microsecond");
        Plan(new[] { In("W.At", values.Skip(25)) }).Should().BeSameAs(InListChunkPlan.Unchanged);
    }

    [Fact]
    public void A_date_offset_list_with_sub_microsecond_ticks_is_refused_past_the_maximum()
    {
        var at = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        var values = Enumerable.Range(0, 50).Select(i => (object?)at.AddMinutes(i)).Append(at.AddTicks(3));

        Plan(new[] { In("W.At", values) }).Refusal.Should().Contain("sub-microsecond");
    }

    [Fact]
    public void A_date_list_mixing_kinds_is_refused_past_the_maximum()
    {
        var at = new DateTime(2026, 11, 7, 12, 0, 0);
        var values = Enumerable.Range(0, 50).Select(i => (object?)DateTime.SpecifyKind(at.AddMinutes(i), i % 2 == 0 ? DateTimeKind.Utc : DateTimeKind.Local));

        Plan(new[] { In("W.At", values) }).Refusal.Should().Contain("kinds");
    }

    [Fact]
    public void A_whole_microsecond_date_list_of_one_kind_is_split()
    {
        var at = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);

        Plan(new[] { In("W.At", Enumerable.Range(0, 50).Select(i => (object?)at.AddTicks(i * 10))) }).IsChunked.Should().BeTrue();
    }

    [Fact]
    public void A_list_on_a_column_the_update_writes_is_kept_whole_within_the_maximum_and_refused_past_it()
    {
        var within = InListChunkPlanner.Plan("update", new[] { In("W.Score", Enumerable.Range(0, 30).Select(i => (object?)i)) },
            Max, Chunk, Probe(), writtenColumns: new[] { "Score" });
        var past = InListChunkPlanner.Plan("update", new[] { In("W.Score", Enumerable.Range(0, 45).Select(i => (object?)i)) },
            Max, Chunk, Probe(), writtenColumns: new[] { "Score" });
        var other = InListChunkPlanner.Plan("update", new[] { In("W.Score", Enumerable.Range(0, 30).Select(i => (object?)i)) },
            Max, Chunk, Probe(), writtenColumns: new[] { "Name" });

        within.Should().BeSameAs(InListChunkPlan.Unchanged);
        past.Refusal.Should().Contain("writes the filtered column");
        other.IsChunked.Should().BeTrue();
    }

    [Fact]
    public void Other_parameters_that_leave_no_room_are_refused()
    {
        var plan = Plan(new[] { In("W.Guid", Guids(15)) }, extra: 40);

        plan.Refusal.Should().Contain("leave no room");
    }

    [Fact]
    public void A_larger_list_that_cannot_be_split_does_not_stop_a_smaller_one_that_can()
    {
        var names = Enumerable.Range(0, 30).Select(i => (object?)("n" + i)).ToList();
        var ids = Guids(20);

        var plan = InListChunkPlanner.Plan("read", new[] { In("W.Name", names), In("W.Guid", ids) }, 45, Chunk, Probe());

        plan.IsChunked.Should().BeTrue();
        ChunkValues(plan, "W.Guid").SelectMany(c => c).Should().Equal(ids);
        plan.Chunks!.Should().AllSatisfy(chunk => FindIn(chunk, "W.Name")!.Values!.Cast<object?>().Should().HaveCount(30));
    }
}
