using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.SQL.Caching;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// SH-H004 and SH-H005 — the two ways a SQL cache key failed to identify the query it stood for.
/// </summary>
/// <remarks>
/// <para>
/// <b>SH-H004.</b> The store keyed on <c>filter.ToString()</c> of the <b>raw</b> expression. Measured
/// 2026-09-09: a closure-captured local renders as
/// <c>value(&lt;&gt;c__DisplayClass5_0).tenant</c> — byte-identical for every captured value — so
/// <c>x =&gt; x.TenantGuid == tenant</c> produced <b>one key for all tenants</b> and the first tenant's
/// rows were served to every other. Inline literals rendered distinctly, which is why the defect was
/// invisible in any test using constants.
/// </para>
/// <para>
/// <b>SH-H005.</b> Keys were table-relative only, so two stores pointed at different databases but
/// sharing one <c>ICache</c> computed identical keys and each served the other's rows.
/// </para>
/// <para>
/// ⚠ <b>Why funcletization alone was not the fix.</b> Folding parameter-free subtrees to constants makes
/// captured <i>scalars</i> distinct, but measured: <c>List&lt;int&gt;{1,2,3}</c> and <c>{9,9,9}</c> both
/// render <c>value(System.Collections.Generic.List`1[System.Int32])</c>, so a set-membership filter would
/// still have collided across different id sets. The rendering is therefore <b>checked</b>, and a filter
/// that cannot be described distinctly is <b>not cached at all</b> — a miss is always correct, a shared
/// key is the defect.
/// </para>
/// </remarks>
public class CrossDatabaseAndFilterKeyingTests
{
    private sealed class Row
    {
        public Guid TenantGuid { get; set; }
        public int N { get; set; }
        public string Name { get; set; } = "";
    }

    private static Expression<Func<Row, bool>> ByTenant(Guid tenant) => x => x.TenantGuid == tenant;
    private static Expression<Func<Row, bool>> ByName(string name) => x => x.Name == name;
    private static Expression<Func<Row, bool>> InSet(List<int> ids) => x => ids.Contains(x.N);

    private static string? Describe(Expression<Func<Row, bool>>? filter)
    {
        SqlCacheKeyBuilder.TryDescribeFilter(filter, out var d).Should().BeTrue(
            "this helper is for filters that CAN be described");
        return d;
    }

    // ---- SH-H004: captured values must produce distinct keys ----

    [Fact]
    public void Two_tenants_do_not_share_a_cache_key()
    {
        // THE defect. Before the fix both filters rendered value(<>c__DisplayClass…).tenant and hashed
        // to the same key, so tenant B was served tenant A's rows.
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var keyA = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(ByTenant(a)), null, null, null);
        var keyB = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(ByTenant(b)), null, null, null);

        keyA.Should().NotBe(keyB, "two tenants must never share a cache key");
    }

    [Fact]
    public void The_raw_expression_really_does_collide_so_the_premise_is_pinned()
    {
        // ⚠ The premise the whole finding rests on, asserted rather than assumed: WITHOUT normalisation
        // the two filters are textually identical. If a future runtime changed how a closure renders,
        // this is the test that would say so, rather than the fix silently becoming unnecessary.
        var a = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var b = Guid.Parse("22222222-2222-2222-2222-222222222222");

        ByTenant(a).ToString().Should().Be(ByTenant(b).ToString(),
            "a captured local renders by display-class field name, not by value");
        ByTenant(a).ToString().Should().Contain("value(");
    }

    [Fact]
    public void Captured_strings_are_also_distinguished()
    {
        var keyA = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(ByName("acme")), null, null, null);
        var keyB = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(ByName("globex")), null, null, null);

        keyA.Should().NotBe(keyB);
    }

    [Fact]
    public void Inline_literals_keep_working()
    {
        // Contract pin: these already produced distinct keys, and must continue to.
        var key1 = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(x => x.N == 1), null, null, null);
        var key2 = SqlCacheKeyBuilder.BuildKey("db", "Rows", Describe(x => x.N == 2), null, null, null);

        key1.Should().NotBe(key2);
    }

    // ---- SH-H004: what cannot be described is refused, not guessed ----

    [Fact]
    public void A_set_membership_filter_is_refused_rather_than_keyed()
    {
        // Funcletization folds `ids` to a ConstantExpression whose ToString() is the TYPE name, so two
        // different sets would render alike. Refusing is the whole point: a miss is correct, a shared key
        // is a cross-caller leak. This is the case normalisation alone would have got wrong.
        SqlCacheKeyBuilder.TryDescribeFilter(InSet(new List<int> { 1, 2, 3 }), out var d)
            .Should().BeFalse("a collection constant does not render by value");
        d.Should().BeNull();
    }

    [Fact]
    public void A_null_filter_is_describable_because_everything_is_a_real_query()
    {
        SqlCacheKeyBuilder.TryDescribeFilter(null, out var d).Should().BeTrue();
        d.Should().BeNull("no filter is keyed as the absence of one, not refused");
    }

    // ---- SH-H005: database identity ----

    [Fact]
    public void Two_databases_do_not_share_a_cache_key()
    {
        var onA = SqlCacheKeyBuilder.BuildKey("hostA:appdb", "Rows", null, null, null, null);
        var onB = SqlCacheKeyBuilder.BuildKey("hostB:appdb", "Rows", null, null, null, null);

        onA.Should().NotBe(onB,
            "two stores on different databases sharing one ICache must not serve each other's rows");
    }

    [Fact]
    public void Two_databases_do_not_share_an_invalidation_prefix()
    {
        // The half that must move WITH the key. A scoped key under an unscoped prefix would
        // over-invalidate (harmless); an unscoped key under a scoped prefix would leave entries nothing
        // ever removes — worse than the leak being fixed. So both are asserted.
        var onA = SqlCacheKeyBuilder.GetTablePrefix("hostA:appdb", "Rows");
        var onB = SqlCacheKeyBuilder.GetTablePrefix("hostB:appdb", "Rows");

        onA.Should().NotBe(onB);
    }

    [Fact]
    public void A_keys_prefix_is_still_its_own_invalidation_prefix()
    {
        // The alignment property invalidation depends on: RemoveByPrefixAsync(prefix) must match the keys
        // that store built. Broken alignment is silent — writes stop invalidating and reads go stale.
        var scope = "hostA:appdb";
        var prefix = SqlCacheKeyBuilder.GetTablePrefix(scope, "Rows");

        SqlCacheKeyBuilder.BuildKey(scope, "Rows", null, null, null, null).Should().StartWith(prefix);
        SqlCacheKeyBuilder.BuildKey(scope, "Rows", Describe(x => x.N == 1), "Name:asc", 10, 5)
            .Should().StartWith(prefix);
    }

    [Fact]
    public void The_scope_is_hashed_so_a_connection_string_cannot_leak_into_a_key()
    {
        // Settings.GetId() is Location:Name:UserName:Port. Two reasons it is hashed rather than inlined:
        // its colons would make the key's segments ambiguous, and a cache key is often visible in logs or
        // a Redis keyspace where a host and user name do not belong.
        var prefix = SqlCacheKeyBuilder.GetTablePrefix("dbhost.internal:appdb:svc_user:5432", "Rows");

        prefix.Should().NotContain("dbhost.internal").And.NotContain("svc_user");
        prefix.Split(':')[1].Should().HaveLength(16).And.MatchRegex("^[0-9a-f]{16}$");
    }

    [Fact]
    public void Different_tables_in_one_database_still_differ()
    {
        // Contract pin from before this change; the scope segment must not have flattened it.
        SqlCacheKeyBuilder.GetTablePrefix("db", "Rows")
            .Should().NotBe(SqlCacheKeyBuilder.GetTablePrefix("db", "Others"));
    }
}
