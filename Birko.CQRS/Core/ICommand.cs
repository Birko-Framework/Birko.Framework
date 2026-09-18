namespace Birko.CQRS
{
    /// <summary>
    /// A command that performs a write operation and returns no result.
    /// </summary>
    public interface ICommand : IRequest<Unit>
    {
    }

    /// <summary>
    /// A command that performs a write operation and returns a result.
    /// </summary>
    /// <typeparam name="TResult">The type of result produced by the command.</typeparam>
    public interface ICommand<out TResult> : IRequest<TResult>
    {
    }
}
