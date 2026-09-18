using Birko.Workflow.Core;
using Birko.Workflow.Definition;
using Birko.Workflow.Execution;
using FluentAssertions;
using Xunit;

namespace Birko.Workflow.Tests;

public class TestData
{
    public bool PaymentReceived { get; set; }
    public List<string> ActionLog { get; } = new();
}

public class WorkflowEngineTests
{
    private static WorkflowDefinition<TestData> CreateOrderWorkflow()
    {
        return new WorkflowBuilder<TestData>("OrderProcessing")
            .InitialState("Pending")
            .State("Pending").And()
            .State("Paid").And()
            .State("Shipped").And()
            .State("Delivered").IsFinal().And()
            .State("Cancelled").IsFinal().And()
            .Transition("pay", "Pending", "Paid")
                .Guard(inst => inst.Data.PaymentReceived, "Payment required")
                .And()
            .Transition("ship", "Paid", "Shipped").And()
            .Transition("deliver", "Shipped", "Delivered").And()
            .Transition("cancel", "Pending", "Cancelled").And()
            .Transition("cancel", "Paid", "Cancelled").And()
            .Build();
    }

    [Fact]
    public async Task FireAsync_ValidTransition_ReturnsSuccess()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData { PaymentReceived = true };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var result = await engine.FireAsync(workflow, instance, "pay");

        result.IsSuccess.Should().BeTrue();
        result.FromState.Should().Be("Pending");
        result.ToState.Should().Be("Paid");
        instance.CurrentState.Should().Be("Paid");
    }

    [Fact]
    public async Task FireAsync_GuardFails_ReturnsDenied()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData { PaymentReceived = false };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var result = await engine.FireAsync(workflow, instance, "pay");

        result.IsDenied.Should().BeTrue();
        result.DenialReasons.Should().Contain("Payment required");
        instance.CurrentState.Should().Be("Pending");
    }

    [Fact]
    public async Task FireAsync_NoMatchingTransition_ReturnsNotFound()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var result = await engine.FireAsync(workflow, instance, "ship");

        result.IsNotFound.Should().BeTrue();
    }

    [Fact]
    public async Task FireAsync_FinalState_SetsCompleted()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "cancel");

        instance.CurrentState.Should().Be("Cancelled");
        instance.Status.Should().Be(WorkflowStatus.Completed);
    }

    [Fact]
    public async Task FireAsync_CompletedInstance_Throws()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "cancel");

        var act = async () => await engine.FireAsync(workflow, instance, "pay");
        await act.Should().ThrowAsync<WorkflowCompletedException>();
    }

    [Fact]
    public async Task FireAsync_RecordsHistory()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData { PaymentReceived = true };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "pay");
        await engine.FireAsync(workflow, instance, "ship");

        instance.History.Should().HaveCount(2);
        instance.History[0].FromState.Should().Be("Pending");
        instance.History[0].ToState.Should().Be("Paid");
        instance.History[0].Trigger.Should().Be("pay");
        instance.History[1].FromState.Should().Be("Paid");
        instance.History[1].ToState.Should().Be("Shipped");
    }

    [Fact]
    public async Task FireAsync_ExecutesActionsInOrder()
    {
        var workflow = new WorkflowBuilder<TestData>("ActionOrder")
            .InitialState("A")
            .State("A")
                .OnExit(async (inst, ct) => inst.Data.ActionLog.Add("A-exit"))
                .And()
            .State("B")
                .OnEntry(async (inst, ct) => inst.Data.ActionLog.Add("B-entry"))
                .IsFinal()
                .And()
            .Transition("go", "A", "B")
                .Action(async (inst, ct) => inst.Data.ActionLog.Add("transition"))
                .And()
            .Build();

        var engine = new WorkflowEngine();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "go");

        data.ActionLog.Should().Equal("A-exit", "transition", "B-entry");
    }

    [Fact]
    public async Task FireAsync_ActionThrows_SetsFaulted()
    {
        var workflow = new WorkflowBuilder<TestData>("Faulting")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B")
                .Action(async (inst, ct) => throw new InvalidOperationException("boom"))
                .And()
            .Build();

        var engine = new WorkflowEngine();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var act = async () => await engine.FireAsync(workflow, instance, "go");
        await act.Should().ThrowAsync<WorkflowActionException>()
            .WithInnerException<WorkflowActionException, InvalidOperationException>();

        instance.Status.Should().Be(WorkflowStatus.Faulted);
    }

    [Fact]
    public async Task FireAsync_OnEntryThrows_RollsBackCurrentStateAndAppendsNoHistory()
    {
        // CR-M267: CurrentState was advanced before OnEntry ran; if OnEntry faults, the instance must
        // not report a state its History doesn't contain. It should roll back to the from-state.
        var workflow = new WorkflowBuilder<TestData>("EntryFault")
            .InitialState("A")
            .State("A").And()
            .State("B")
                .OnEntry(async (inst, ct) => throw new InvalidOperationException("entry boom"))
                .IsFinal()
                .And()
            .Transition("go", "A", "B").And()
            .Build();

        var engine = new WorkflowEngine();
        var instance = WorkflowInstance<TestData>.Create(workflow, new TestData());

        var act = async () => await engine.FireAsync(workflow, instance, "go");
        await act.Should().ThrowAsync<WorkflowActionException>();

        instance.Status.Should().Be(WorkflowStatus.Faulted);
        instance.CurrentState.Should().Be("A", "a faulted OnEntry must not leave a half-applied state (CR-M267)");
        instance.History.Should().BeEmpty("no transition record is appended when OnEntry faults");
    }

    [Fact]
    public async Task FireAsync_OnEntryThrows_ReportsToStateInException()
    {
        // CR-L400: when the destination's OnEntry action faults, the exception's State must
        // name the state whose action threw (the to-state), not the origin.
        var workflow = new WorkflowBuilder<TestData>("EntryFaultState")
            .InitialState("A")
            .State("A").And()
            .State("B")
                .OnEntry(async (inst, ct) => throw new InvalidOperationException("entry boom"))
                .IsFinal()
                .And()
            .Transition("go", "A", "B").And()
            .Build();

        var engine = new WorkflowEngine();
        var instance = WorkflowInstance<TestData>.Create(workflow, new TestData());

        var act = async () => await engine.FireAsync(workflow, instance, "go");
        var ex = (await act.Should().ThrowAsync<WorkflowActionException>()).Which;
        ex.State.Should().Be("B", "the OnEntry action of the to-state failed");
        ex.Trigger.Should().Be("go");
    }

    [Fact]
    public async Task FireAsync_ExitOrTransitionActionThrows_ReportsFromStateInException()
    {
        // CR-L400 companion: an exit/transition action failure is still reported against the from-state.
        var workflow = new WorkflowBuilder<TestData>("TransitionFaultState")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B")
                .Action(async (inst, ct) => throw new InvalidOperationException("boom"))
                .And()
            .Build();

        var engine = new WorkflowEngine();
        var instance = WorkflowInstance<TestData>.Create(workflow, new TestData());

        var act = async () => await engine.FireAsync(workflow, instance, "go");
        var ex = (await act.Should().ThrowAsync<WorkflowActionException>()).Which;
        ex.State.Should().Be("A", "the transition action ran in the from-state");
    }

    [Fact]
    public async Task FireAsync_PreCancelledToken_ThrowsWithoutMutating()
    {
        // CR-L401: a pre-cancelled token must surface OperationCanceledException even for a
        // guard-only / actionless transition that would otherwise complete silently.
        var workflow = new WorkflowBuilder<TestData>("CancelObserving")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B").And()
            .Build();

        var engine = new WorkflowEngine();
        var instance = WorkflowInstance<TestData>.Create(workflow, new TestData());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await engine.FireAsync(workflow, instance, "go", cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        instance.CurrentState.Should().Be("A", "a cancelled fire must not advance state");
        instance.Status.Should().Be(WorkflowStatus.Active);
    }

    [Fact]
    public async Task FireAsync_FaultedInstance_Throws()
    {
        var workflow = new WorkflowBuilder<TestData>("Faulting")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B")
                .Action(async (inst, ct) => throw new InvalidOperationException("boom"))
                .And()
            .Build();

        var engine = new WorkflowEngine();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        try { await engine.FireAsync(workflow, instance, "go"); } catch { }

        var act = async () => await engine.FireAsync(workflow, instance, "go");
        await act.Should().ThrowAsync<WorkflowFaultedException>();
    }

    [Fact]
    public async Task FireAsync_InvokesStateChangedCallback()
    {
        StateChangeRecord? captured = null;
        string? capturedName = null;

        var engine = new WorkflowEngine((record, name, id) =>
        {
            captured = record;
            capturedName = name;
        });

        var workflow = CreateOrderWorkflow();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "cancel");

        captured.Should().NotBeNull();
        captured!.FromState.Should().Be("Pending");
        captured.ToState.Should().Be("Cancelled");
        capturedName.Should().Be("OrderProcessing");
    }

    [Fact]
    public void GetPermittedTriggers_GuardFails_ExcludesGuardedTrigger()
    {
        // CR-L399: the engine overload is guard-aware. From "Pending", "pay" is guarded on
        // PaymentReceived — with it false the guard fails, so "pay" must NOT be reported
        // (FireAsync would deny it); only the unguarded "cancel" is permitted.
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData { PaymentReceived = false };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var triggers = engine.GetPermittedTriggers(workflow, instance);

        triggers.Should().BeEquivalentTo("cancel");
    }

    [Fact]
    public void GetPermittedTriggers_GuardPasses_IncludesGuardedTrigger()
    {
        // CR-L399: with the guard satisfied, the guarded trigger is reported alongside
        // the unguarded ones — matching what FireAsync will actually accept.
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData { PaymentReceived = true };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var triggers = engine.GetPermittedTriggers(workflow, instance);

        triggers.Should().BeEquivalentTo("pay", "cancel");
    }

    [Fact]
    public async Task GetPermittedTriggers_CompletedInstance_ReturnsEmpty()
    {
        var engine = new WorkflowEngine();
        var workflow = CreateOrderWorkflow();
        var data = new TestData();
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        await engine.FireAsync(workflow, instance, "cancel");

        var triggers = engine.GetPermittedTriggers(workflow, instance);
        triggers.Should().BeEmpty();
    }

    [Fact]
    public async Task MultipleGuards_AllMustPass()
    {
        var workflow = new WorkflowBuilder<TestData>("MultiGuard")
            .InitialState("A")
            .State("A").And()
            .State("B").IsFinal().And()
            .Transition("go", "A", "B")
                .Guard(inst => inst.Data.PaymentReceived, "Payment required")
                .Guard(inst => inst.Data.ActionLog.Count > 0, "Actions required")
                .And()
            .Build();

        var engine = new WorkflowEngine();
        var data = new TestData { PaymentReceived = true };
        var instance = WorkflowInstance<TestData>.Create(workflow, data);

        var result = await engine.FireAsync(workflow, instance, "go");

        result.IsDenied.Should().BeTrue();
        result.DenialReasons.Should().Contain("Actions required");
    }
}
