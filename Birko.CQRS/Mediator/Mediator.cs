using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.CQRS.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.CQRS
{
    /// <summary>
    /// Default mediator implementation that resolves handlers and pipeline behaviors from DI.
    /// </summary>
    public class Mediator : IMediator
    {
        private readonly IServiceProvider _serviceProvider;
        // Keyed on (requestType, resultType): the wrapper bakes in TResult, so caching by request
        // type alone returned a wrapper built for a different TResult on covariant dispatch,
        // throwing InvalidCastException / resolving the wrong handler (CR-H039).
        private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), RequestHandlerBase> _handlerCache = new();

        public Mediator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <inheritdoc />
        public Task<TResult> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var requestType = request.GetType();
            var handler = _handlerCache.GetOrAdd((requestType, typeof(TResult)), static key => CreateHandler(key.RequestType, key.ResultType));
            return ((RequestHandler<TResult>)handler).HandleAsync(request, _serviceProvider, cancellationToken);
        }

        /// <inheritdoc />
        public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
        {
            await SendAsync<Unit>(command, cancellationToken).ConfigureAwait(false);
        }

        private static RequestHandlerBase CreateHandler(Type requestType, Type resultType)
        {
            var handlerType = typeof(RequestHandlerWrapper<,>).MakeGenericType(requestType, resultType);
            return (RequestHandlerBase)Activator.CreateInstance(handlerType)!;
        }

        private abstract class RequestHandlerBase
        {
        }

        private abstract class RequestHandler<TResult> : RequestHandlerBase
        {
            public abstract Task<TResult> HandleAsync(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken);
        }

        private class RequestHandlerWrapper<TRequest, TResult> : RequestHandler<TResult> where TRequest : IRequest<TResult>
        {
            public override Task<TResult> HandleAsync(IRequest<TResult> request, IServiceProvider serviceProvider, CancellationToken cancellationToken)
            {
                var handler = serviceProvider.GetService<IRequestHandler<TRequest, TResult>>();
                if (handler == null)
                {
                    throw new InvalidOperationException(
                        $"No handler registered for {typeof(TRequest).Name}. " +
                        $"Register an IRequestHandler<{typeof(TRequest).Name}, {typeof(TResult).Name}> in the service collection.");
                }

                var behaviors = serviceProvider.GetServices<IPipelineBehavior<TRequest, TResult>>();
                var pipeline = new RequestPipeline<TRequest, TResult>(behaviors);

                return pipeline.ExecuteAsync(
                    (TRequest)request,
                    ct => handler.HandleAsync((TRequest)request, ct),
                    cancellationToken);
            }
        }
    }
}
