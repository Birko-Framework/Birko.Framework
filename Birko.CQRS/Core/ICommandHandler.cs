using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS
{
    /// <summary>
    /// Handles a command that returns no result.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to handle.</typeparam>
    public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit> where TCommand : ICommand
    {
    }

    /// <summary>
    /// Handles a command that returns a result.
    /// </summary>
    /// <typeparam name="TCommand">The type of command to handle.</typeparam>
    /// <typeparam name="TResult">The type of result produced.</typeparam>
    public interface ICommandHandler<in TCommand, TResult> : IRequestHandler<TCommand, TResult> where TCommand : ICommand<TResult>
    {
    }
}
