namespace Birko.CQRS
{
    /// <summary>
    /// Marker interface for all requests (commands and queries) dispatched through the mediator.
    /// </summary>
    /// <typeparam name="TResult">The type of result produced by handling this request.</typeparam>
    public interface IRequest<out TResult>
    {
    }
}
