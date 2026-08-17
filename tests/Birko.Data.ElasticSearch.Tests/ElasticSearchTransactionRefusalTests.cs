using System;
using System.Linq;
using Birko.Data.ElasticSearch.Stores;
using Birko.Data.ElasticSearch.UnitOfWork;
using Birko.Data.Models;
using Birko.Data.Patterns.UnitOfWork;
using Birko.Data.Stores;
using FluentAssertions;
using Nest;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests;

/// <summary>
/// TASK-240 — ElasticSearch's half of the per-provider transaction proof: it <b>refuses</b> a boundary
/// rather than accepting one and ignoring it.
///
/// <para>
/// Elasticsearch has no transaction concept. The honest "no" here is structural: the store does not
/// implement <see cref="IAsyncTransactionalStore{T, TContext}"/> at all, so there is no
/// <c>SetTransactionContext</c> to call and no way to believe a boundary is in force. That is a stronger
/// guarantee than a method that throws, because it fails at compile time.
/// </para>
///
/// <para>
/// The absence is asserted rather than assumed. "I didn't add it to the interface" is construction, not
/// evidence — the next person to widen the interface would otherwise break this silently, which is the
/// standing rule from the Redis whole-database-delete work (SH-H006).
/// </para>
///
/// <para>
/// None of this needs a live server: the claim is about the type system and about what the unit of work
/// declares, so gating it would make the one assertion that matters skippable.
/// </para>
/// </summary>
public class ElasticSearchTransactionRefusalTests
{
    public class Doc : AbstractModel
    {
        public string? Name { get; set; }
    }

    // ---------------------------------------------------------------- the refusal is structural

    [Fact]
    public void The_async_store_does_not_accept_a_transaction_context_at_all()
    {
        var implemented = typeof(AsyncElasticSearchStore<Doc>)
            .GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .ToList();

        implemented.Should().NotContain(typeof(IAsyncTransactionalStore<,>),
            "ElasticSearch cannot honour a transaction boundary, so it must not offer a hook that accepts "
          + "one and drops it — the SQL async store did exactly that, and a hook that reads as available "
          + "is worse than an absent feature");
    }

    [Fact]
    public void The_sync_store_does_not_accept_a_transaction_context_either()
    {
        var implemented = typeof(ElasticSearchStore<Doc>)
            .GetInterfaces()
            .Where(i => i.IsGenericType)
            .Select(i => i.GetGenericTypeDefinition())
            .ToList();

        implemented.Should().NotContain(typeof(ITransactionalStore<,>));
    }

    [Fact]
    public void Neither_store_exposes_a_SetTransactionContext_method_under_any_name()
    {
        // Belt and braces: an implicit interface implementation would show up here even if someone added
        // it without the interface.
        typeof(AsyncElasticSearchStore<Doc>).GetMethod("SetTransactionContext")
            .Should().BeNull();
        typeof(ElasticSearchStore<Doc>).GetMethod("SetTransactionContext")
            .Should().BeNull();
    }

    // ---------------------------------------------------------------- what the UoW does promise

    [Fact]
    public void The_unit_of_work_declares_itself_non_atomic_rather_than_pretending()
    {
        var uow = new ElasticSearchUnitOfWork(new ElasticClient());

        uow.Capabilities.Atomicity.Should().Be(TransactionAtomicity.BestEffort,
            "the Bulk API batches; it does not make the batch atomic");
        uow.Capabilities.Scope.Should().Be(TransactionBoundaryScope.None);
        uow.Capabilities.ReadsSeeUncommittedWrites.Should().BeFalse();
        uow.Capabilities.Limitations.Should().Contain("no transactions");
    }

    [Fact]
    public void A_caller_can_tell_atomic_backends_from_non_atomic_ones_without_naming_them()
    {
        // The point of the capability contract: a caller decides from the declaration, not from a
        // hard-coded list of backend names it has to keep in sync.
        var elastic = new ElasticSearchUnitOfWork(new ElasticClient()).Capabilities;

        (elastic.Atomicity == TransactionAtomicity.Atomic).Should().BeFalse();
        elastic.Limitations.Should().NotBeNullOrWhiteSpace(
            "a backend that cannot honour a boundary must say why, not merely return a lower enum value");
    }

    // ---------------------------------------------------------------- rollback is honest about itself

    [Fact]
    public void Rollback_before_commit_discards_the_queued_operations()
    {
        var uow = new ElasticSearchUnitOfWork(new ElasticClient());

        uow.BeginAsync().GetAwaiter().GetResult();
        uow.IsActive.Should().BeTrue();
        uow.Context!.Index(new Doc { Guid = Guid.NewGuid(), Name = "queued" });

        // Nothing has been sent yet, so this really does undo it. After CommitAsync it would not — which
        // is precisely what Atomicity.BestEffort is telling a caller.
        uow.RollbackAsync().GetAwaiter().GetResult();
        uow.IsActive.Should().BeFalse();
        uow.Context.Should().BeNull();
    }

    [Fact]
    public void Rollback_without_an_active_batch_is_refused()
    {
        var uow = new ElasticSearchUnitOfWork(new ElasticClient());

        var act = () => uow.RollbackAsync().GetAwaiter().GetResult();
        act.Should().Throw<NoActiveTransactionException>();
    }
}
