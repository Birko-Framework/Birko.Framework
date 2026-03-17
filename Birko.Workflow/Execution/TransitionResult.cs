namespace Birko.Workflow.Execution;

public sealed class TransitionResult
{
    public bool IsSuccess { get; }
    public bool IsDenied { get; }
    public bool IsNotFound { get; }
    public string? FromState { get; }
    public string? ToState { get; }
    public string? Trigger { get; }
    public IReadOnlyList<string> DenialReasons { get; }

    private TransitionResult(
        bool isSuccess,
        bool isDenied,
        bool isNotFound,
        string? fromState,
        string? toState,
        string? trigger,
        IReadOnlyList<string>? denialReasons)
    {
        IsSuccess = isSuccess;
        IsDenied = isDenied;
        IsNotFound = isNotFound;
        FromState = fromState;
        ToState = toState;
        Trigger = trigger;
        DenialReasons = denialReasons ?? Array.Empty<string>();
    }

    public static TransitionResult Success(string fromState, string toState, string trigger) =>
        new(true, false, false, fromState, toState, trigger, null);

    public static TransitionResult Denied(string fromState, string trigger, IReadOnlyList<string> reasons) =>
        new(false, true, false, fromState, null, trigger, reasons);

    public static TransitionResult NotFound(string fromState, string trigger) =>
        new(false, false, true, fromState, null, trigger, null);
}
