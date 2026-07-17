using Birko.Workflow.Core;

namespace Birko.Workflow.Execution;

public sealed class WorkflowEngine : IWorkflowEngine
{
    private readonly Action<StateChangeRecord, string, Guid>? _onStateChanged;

    public WorkflowEngine(Action<StateChangeRecord, string, Guid>? onStateChanged = null)
    {
        _onStateChanged = onStateChanged;
    }

    public async Task<TransitionResult> FireAsync<TData>(
        IWorkflowDefinition<TData> definition,
        IWorkflowInstance<TData> instance,
        string trigger,
        CancellationToken cancellationToken = default)
    {
        // Observe an already-cancelled token before any work — a guard-only / pure
        // state-change transition has no actions to propagate the token, so without
        // this an OperationCanceledException would be silently swallowed (matches the
        // framework convention: throw at the top of every public async path).
        cancellationToken.ThrowIfCancellationRequested();

        if (instance is not WorkflowInstance<TData> mutableInstance)
        {
            throw new ArgumentException("Instance must be created via WorkflowInstance<TData>.Create().", nameof(instance));
        }

        if (instance.Status == WorkflowStatus.Completed)
        {
            throw new WorkflowCompletedException(definition.Name, instance.InstanceId);
        }
        if (instance.Status == WorkflowStatus.Faulted)
        {
            throw new WorkflowFaultedException(definition.Name, instance.InstanceId);
        }

        var transition = definition.Transitions
            .FirstOrDefault(t => t.FromState == instance.CurrentState && t.Trigger == trigger);

        if (transition == null)
        {
            return TransitionResult.NotFound(instance.CurrentState, trigger);
        }

        var denialReasons = new List<string>();
        foreach (var guard in transition.Guards)
        {
            if (!guard.Predicate(instance))
            {
                denialReasons.Add(guard.Reason);
            }
        }

        if (denialReasons.Count > 0)
        {
            return TransitionResult.Denied(instance.CurrentState, trigger, denialReasons);
        }

        var fromState = instance.CurrentState;
        var fromStateDef = definition.States.FirstOrDefault(s => s.Name == fromState);
        var toStateDef = definition.States.FirstOrDefault(s => s.Name == transition.ToState);

        // Tracks the state whose action is currently running, so a failure is reported
        // against the actual failing phase: fromState for exit/transition actions,
        // toState once the destination's OnEntry actions run (CR-L400).
        var actionState = fromState;

        try
        {
            if (fromStateDef != null)
            {
                foreach (var action in fromStateDef.OnExitActions)
                {
                    await action(instance, cancellationToken).ConfigureAwait(false);
                }
            }

            foreach (var action in transition.Actions)
            {
                await action(instance, cancellationToken).ConfigureAwait(false);
            }

            mutableInstance.CurrentState = transition.ToState;
            actionState = transition.ToState;

            if (toStateDef != null)
            {
                foreach (var action in toStateDef.OnEntryActions)
                {
                    await action(instance, cancellationToken).ConfigureAwait(false);
                }
            }

            if (toStateDef?.IsFinal == true)
            {
                mutableInstance.Status = WorkflowStatus.Completed;
            }

            var record = new StateChangeRecord(fromState, transition.ToState, trigger, DateTime.UtcNow);
            mutableInstance.AddHistoryRecord(record);

            _onStateChanged?.Invoke(record, definition.Name, instance.InstanceId);

            return TransitionResult.Success(fromState, transition.ToState, trigger);
        }
        catch (Exception ex) when (ex is not WorkflowException)
        {
            // CR-M267: CurrentState was advanced to ToState before the OnEntry actions ran, but the
            // history record is only appended after they succeed. If an OnEntry action faults, roll
            // CurrentState back to fromState so a faulted instance never reports a state its History
            // doesn't contain (an observable inconsistency once the faulted instance is persisted).
            mutableInstance.CurrentState = fromState;
            mutableInstance.Status = WorkflowStatus.Faulted;
            throw new WorkflowActionException(
                definition.Name,
                instance.InstanceId,
                actionState,
                trigger,
                ex);
        }
    }

    public IReadOnlyList<string> GetPermittedTriggers<TData>(
        IWorkflowDefinition<TData> definition,
        IWorkflowInstance<TData> instance)
    {
        if (instance.Status is WorkflowStatus.Completed or WorkflowStatus.Faulted)
        {
            return Array.Empty<string>();
        }

        // Unlike the definition's state-only GetPermittedTriggers, the engine overload
        // has the instance, so it also evaluates guards — a trigger is permitted only if
        // the transition FireAsync would select for it (the first one matching
        // FromState+Trigger, per its FirstOrDefault) passes all its guards. This keeps
        // the reported triggers in sync with what FireAsync will actually accept (CR-L399).
        return definition.Transitions
            .Where(t => t.FromState == instance.CurrentState)
            .GroupBy(t => t.Trigger)
            .Where(g => g.First().Guards.All(guard => guard.Predicate(instance)))
            .Select(g => g.Key)
            .ToList();
    }
}
