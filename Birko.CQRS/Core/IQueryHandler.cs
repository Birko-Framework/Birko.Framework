using System.Threading;
using System.Threading.Tasks;

namespace Birko.CQRS
{
    /// <summary>
    /// Handles a query and returns a result.
    /// </summary>
    /// <typeparam name="TQuery">The type of query to handle.</typeparam>
    /// <typeparam name="TResult">The type of result produced.</typeparam>
    public interface IQueryHandler<in TQuery, TResult> : IRequestHandler<TQuery, TResult> where TQuery : IQuery<TResult>
    {
    }
}
