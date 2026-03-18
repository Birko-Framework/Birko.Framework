namespace Birko.CQRS
{
    /// <summary>
    /// A query that reads data and returns a result. Queries must not modify state.
    /// </summary>
    /// <typeparam name="TResult">The type of result produced by the query.</typeparam>
    public interface IQuery<out TResult> : IRequest<TResult>
    {
    }
}
