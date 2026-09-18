using Birko.AI.Orchestration.Models;
using Microsoft.Extensions.Logging;

namespace Birko.AI.Orchestration.Dispatch
{
    /// <summary>
    /// In-process task dispatcher. Tasks are executed via callback.
    /// Default dispatcher when a message queue is not configured.
    /// </summary>
    public class DirectTaskDispatcher : ITaskDispatcher
    {
        private readonly Func<TaskAssignment, CancellationToken, Task>? _dispatchCallback;
        private readonly ILogger? _logger;

        public bool IsDistributed => false;

        public DirectTaskDispatcher(
            Func<TaskAssignment, CancellationToken, Task>? dispatchCallback = null,
            ILogger? logger = null)
        {
            _dispatchCallback = dispatchCallback;
            _logger = logger;
        }

        public async Task DispatchTaskAsync(TaskAssignment assignment, CancellationToken cancellationToken = default)
        {
            _logger?.LogDebug("Dispatching task {TaskId} ({AgentType}) for project {ProjectId} in-process",
                assignment.TaskId, assignment.AgentType, assignment.ProjectId);

            if (_dispatchCallback != null)
            {
                await _dispatchCallback(assignment, cancellationToken);
            }
            else
            {
                _logger?.LogWarning("DirectTaskDispatcher has no callback — task {TaskId} was not dispatched", assignment.TaskId);
            }
        }
    }
}
