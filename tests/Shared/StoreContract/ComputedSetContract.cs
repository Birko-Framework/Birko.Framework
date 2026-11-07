using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>
/// TASK-826 — a computed set (<c>PropertyUpdate.Compute</c>) stores exactly what C# computes on the same row, on every
/// provider. The rows sit on rounding midpoints (<c>0.15 / 1.2 = 0.125</c>), where C#'s and SQL's default modes disagree,
/// beside an inexact quotient (<c>100 / 1.2</c>) and a negative one; both modes are run, and the expected value is C#'s
/// <see cref="Math.Round(decimal, int, MidpointRounding)"/> on the stored inputs.
///
/// <para>Shared source, linked into each provider's test project, which subclasses it with its own store and a row type
/// holding a code and two <c>decimal</c> columns with at least four decimals.</para>
/// </summary>
public abstract class ComputedSetContract<T> where T : AbstractModel
{
    /// <summary>Gross values: three midpoints after <c>/ 1.2</c> (one negative), an inexact one, a plain one.</summary>
    public static readonly decimal[] Grosses = { 0.15m, 0.162m, -0.15m, 100.00m, 2.5m, 0.3m };

    /// <summary>Net values: <c>1.0375 * 1.2 = 1.245</c> and <c>-1.0375 * 1.2</c> are midpoints.</summary>
    public static readonly decimal[] Nets = { 1.0375m, -1.0375m, 2.0625m, 7m };

    public const decimal Factor = 1.2m;

    protected virtual bool Ready() => true;

    protected virtual void Settle(IBulkStore<T> store) { }

    /// <summary>A fresh, empty, initialised store.</summary>
    protected abstract IBulkStore<T> NewStore();

    protected abstract T NewRow(string code, decimal gross, decimal net);

    protected abstract Expression<Func<T, string?>> Code { get; }
    protected abstract Expression<Func<T, decimal>> Gross { get; }
    protected abstract Expression<Func<T, decimal>> Net { get; }

    [Theory]
    [InlineData(MidpointRounding.ToEven)]
    [InlineData(MidpointRounding.AwayFromZero)]
    public void Contract_Computed_division_rounds_as_CSharp_does(MidpointRounding mode)
    {
        if (!Ready()) return;
        var store = NewStore();
        for (var i = 0; i < Grosses.Length; i++) store.Create(NewRow("g" + i, Grosses[i], 0m));
        Settle(store);

        store.Update(CodeStartsWith("g"), new PropertyUpdate<T>()
            .Compute(Net, Formula(Gross, ComputedOp.Divide, mode))
            .Set(Code, "done"));
        Settle(store);

        var rows = store.Read(CodeIs("done"), null, null, null).ToList();
        rows.Should().HaveCount(Grosses.Length, "every matched row is updated, and the constant set lands beside the formula");
        foreach (var row in rows)
        {
            var gross = Gross.Compile()(row);
            Net.Compile()(row).Should().Be(Math.Round(gross / Factor, 2, mode), $"Round({gross} / {Factor}, 2, {mode})");
        }
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven)]
    [InlineData(MidpointRounding.AwayFromZero)]
    public void Contract_Computed_multiplication_rounds_as_CSharp_does(MidpointRounding mode)
    {
        if (!Ready()) return;
        var store = NewStore();
        for (var i = 0; i < Nets.Length; i++) store.Create(NewRow("n" + i, 0m, Nets[i]));
        Settle(store);

        store.Update(CodeStartsWith("n"), new PropertyUpdate<T>().Compute(Gross, Formula(Net, ComputedOp.Multiply, mode)));
        Settle(store);

        var rows = store.Read(CodeStartsWith("n"), null, null, null).ToList();
        rows.Should().HaveCount(Nets.Length);
        foreach (var row in rows)
        {
            var net = Net.Compile()(row);
            Gross.Compile()(row).Should().Be(Math.Round(net * Factor, 2, mode), $"Round({net} * {Factor}, 2, {mode})");
        }
    }

    [Fact]
    public void Contract_Computed_set_writes_only_the_rows_the_filter_matches()
    {
        if (!Ready()) return;
        var store = NewStore();
        store.Create(NewRow("in", 0.15m, 0m));
        store.Create(NewRow("out", 0.15m, 9m));
        Settle(store);

        store.Update(CodeIs("in"), new PropertyUpdate<T>().Compute(Net, Formula(Gross, ComputedOp.Divide, MidpointRounding.ToEven)));
        Settle(store);

        Net.Compile()(store.ReadFirst(CodeIs("in"))!).Should().Be(0.12m);
        Net.Compile()(store.ReadFirst(CodeIs("out"))!).Should().Be(9m);
    }

    // The formula built from the provider's own selectors: Math.Round(column op 1.2, 2, mode).
    public enum ComputedOp { Divide, Multiply }

    public static Expression<Func<T, decimal>> Formula(Expression<Func<T, decimal>> column, ComputedOp op, MidpointRounding mode)
    {
        var x = column.Parameters[0];
        var factor = Expression.Constant(Factor);
        Expression body = op == ComputedOp.Divide ? Expression.Divide(column.Body, factor) : Expression.Multiply(column.Body, factor);
        var round = typeof(Math).GetMethod(nameof(Math.Round), new[] { typeof(decimal), typeof(int), typeof(MidpointRounding) })!;
        return Expression.Lambda<Func<T, decimal>>(
            Expression.Call(round, body, Expression.Constant(2), Expression.Constant(mode)), x);
    }

    protected Expression<Func<T, bool>> CodeIs(string value)
    {
        var code = Code; // one instance: its body and parameter must belong together (MongoDB's LINQ checks)
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(code.Body, Expression.Constant(value, typeof(string))), code.Parameters);
    }

    /// <summary>Codes are short and few, so "starts with" is an explicit list: every provider translates <c>||</c> of equalities.</summary>
    protected Expression<Func<T, bool>> CodeStartsWith(string prefix)
    {
        var count = prefix == "g" ? Grosses.Length : Nets.Length;
        var code = Code;
        Expression? body = null;
        for (var i = 0; i < count; i++)
        {
            var equal = Expression.Equal(code.Body, Expression.Constant(prefix + i, typeof(string)));
            body = body == null ? equal : Expression.OrElse(body, equal);
        }
        return Expression.Lambda<Func<T, bool>>(body!, code.Parameters);
    }
}

/// <summary>The async twin, on <see cref="IAsyncBulkStore{T}"/>.</summary>
public abstract class AsyncComputedSetContract<T> where T : AbstractModel
{
    protected virtual bool Ready() => true;

    protected virtual void Settle(IAsyncBulkStore<T> store) { }

    protected abstract IAsyncBulkStore<T> NewStore();

    protected abstract T NewRow(string code, decimal gross, decimal net);

    protected abstract Expression<Func<T, string?>> Code { get; }
    protected abstract Expression<Func<T, decimal>> Gross { get; }
    protected abstract Expression<Func<T, decimal>> Net { get; }

    [Theory]
    [InlineData(MidpointRounding.ToEven)]
    [InlineData(MidpointRounding.AwayFromZero)]
    public async Task Contract_Computed_division_rounds_as_CSharp_does_async(MidpointRounding mode)
    {
        if (!Ready()) return;
        var store = NewStore();
        var grosses = ComputedSetContract<T>.Grosses;
        for (var i = 0; i < grosses.Length; i++) await store.CreateAsync(NewRow("g" + i, grosses[i], 0m));
        Settle(store);

        await store.UpdateAsync(CodeIn("g", grosses.Length), new PropertyUpdate<T>()
            .Compute(Net, ComputedSetContract<T>.Formula(Gross, ComputedSetContract<T>.ComputedOp.Divide, mode))
            .Set(Code, "done"));
        Settle(store);

        var rows = (await store.ReadAsync(CodeIs("done"), null, null, null)).ToList();
        rows.Should().HaveCount(grosses.Length);
        foreach (var row in rows)
        {
            var gross = Gross.Compile()(row);
            Net.Compile()(row).Should().Be(Math.Round(gross / ComputedSetContract<T>.Factor, 2, mode), $"Round({gross} / 1.2, 2, {mode})");
        }
    }

    [Theory]
    [InlineData(MidpointRounding.ToEven)]
    [InlineData(MidpointRounding.AwayFromZero)]
    public async Task Contract_Computed_multiplication_rounds_as_CSharp_does_async(MidpointRounding mode)
    {
        if (!Ready()) return;
        var store = NewStore();
        var nets = ComputedSetContract<T>.Nets;
        for (var i = 0; i < nets.Length; i++) await store.CreateAsync(NewRow("n" + i, 0m, nets[i]));
        Settle(store);

        await store.UpdateAsync(CodeIn("n", nets.Length), new PropertyUpdate<T>()
            .Compute(Gross, ComputedSetContract<T>.Formula(Net, ComputedSetContract<T>.ComputedOp.Multiply, mode)));
        Settle(store);

        var rows = (await store.ReadAsync(CodeIn("n", nets.Length), null, null, null)).ToList();
        rows.Should().HaveCount(nets.Length);
        foreach (var row in rows)
        {
            var net = Net.Compile()(row);
            Gross.Compile()(row).Should().Be(Math.Round(net * ComputedSetContract<T>.Factor, 2, mode), $"Round({net} * 1.2, 2, {mode})");
        }
    }

    private Expression<Func<T, bool>> CodeIs(string value)
    {
        var code = Code; // one instance: its body and parameter must belong together (MongoDB's LINQ checks)
        return Expression.Lambda<Func<T, bool>>(Expression.Equal(code.Body, Expression.Constant(value, typeof(string))), code.Parameters);
    }

    private Expression<Func<T, bool>> CodeIn(string prefix, int count)
    {
        var code = Code;
        Expression? body = null;
        for (var i = 0; i < count; i++)
        {
            var equal = Expression.Equal(code.Body, Expression.Constant(prefix + i, typeof(string)));
            body = body == null ? equal : Expression.OrElse(body, equal);
        }
        return Expression.Lambda<Func<T, bool>>(body!, code.Parameters);
    }
}
