using Birko.AI.Orchestration.Models;

namespace Birko.AI.Orchestration.Dispatch
{
    /// <summary>
    /// Abstraction for dispatching task assignments to agent workers.
    /// Implement for in-process execution or distributed queue.
    /// </summary>
    public interface ITaskDispatcher
    {
        Task DispatchTaskAsync(TaskAssignment assignment, CancellationToken cancellationToken = default);
        bool IsDistributed { get; }
    }
}
