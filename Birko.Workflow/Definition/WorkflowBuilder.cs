namespace Birko.Workflow.Definition;

public sealed class WorkflowBuilder<TData>
{
    private readonly string _name;
    private string? _initialState;
    private readonly List<StateBuilder<TData>> _stateBuilders = new();
    private readonly List<TransitionBuilder<TData>> _transitionBuilders = new();

    public WorkflowBuilder(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Workflow name cannot be null or empty.", nameof(name));
        }
        _name = name;
    }

    public WorkflowBuilder<TData> InitialState(string state)
    {
        _initialState = state;
        return this;
    }

    public StateBuilder<TData> State(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("State name cannot be null or empty.", nameof(name));
        }

        var builder = new StateBuilder<TData>(this, name);
        _stateBuilders.Add(builder);
        return builder;
    }

    public TransitionBuilder<TData> Transition(string trigger, string fromState, string toState)
    {
        if (string.IsNullOrWhiteSpace(trigger))
        {
            throw new ArgumentException("Trigger cannot be null or empty.", nameof(trigger));
        }
        if (string.IsNullOrWhiteSpace(fromState))
        {
            throw new ArgumentException("FromState cannot be null or empty.", nameof(fromState));
        }
        if (string.IsNullOrWhiteSpace(toState))
        {
            throw new ArgumentException("ToState cannot be null or empty.", nameof(toState));
        }

        var builder = new TransitionBuilder<TData>(this, trigger, fromState, toState);
        _transitionBuilders.Add(builder);
        return builder;
    }

    public WorkflowDefinition<TData> Build()
    {
        if (_initialState == null)
        {
            throw new InvalidOperationException("InitialState must be set before building.");
        }

        var states = _stateBuilders.Select(b => b.Build()).ToList();
        var stateNames = new HashSet<string>(states.Select(s => s.Name));

        if (!stateNames.Contains(_initialState))
        {
            throw new InvalidOperationException($"InitialState '{_initialState}' is not defined as a state.");
        }

        var initialStateDef = states.First(s => s.Name == _initialState);
        if (initialStateDef.IsFinal)
        {
            throw new InvalidOperationException($"InitialState '{_initialState}' cannot be a final state.");
        }

        var transitions = _transitionBuilders.Select(b => b.Build()).ToList();

        foreach (var transition in transitions)
        {
            if (!stateNames.Contains(transition.FromState))
            {
                throw new InvalidOperationException($"Transition trigger '{transition.Trigger}' references undefined FromState '{transition.FromState}'.");
            }
            if (!stateNames.Contains(transition.ToState))
            {
                throw new InvalidOperationException($"Transition trigger '{transition.Trigger}' references undefined ToState '{transition.ToState}'.");
            }
        }

        return new WorkflowDefinition<TData>(_name, _initialState, states, transitions);
    }
}
