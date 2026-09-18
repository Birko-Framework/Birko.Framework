namespace Birko.Workflow.Core;

public sealed record StateChangeRecord(
    string FromState,
    string ToState,
    string Trigger,
    DateTime OccurredAt
);
