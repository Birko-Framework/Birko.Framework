using System;
using Birko.CQRS.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.CQRS.Extensions
{
    /// <summary>
    /// DI registration extensions for Birko.CQRS.
    /// </summary>
    public static class CqrsServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the mediator as a scoped service.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCqrs(this IServiceCollection services)
        {
            services.AddScoped<IMediator, Mediator>();
            return services;
        }

        /// <summary>
        /// Registers a command handler (void return).
        /// </summary>
        /// <typeparam name="TCommand">The command type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCommandHandler<TCommand, THandler>(this IServiceCollection services)
            where TCommand : ICommand
            where THandler : class, ICommandHandler<TCommand>
        {
            services.AddTransient<IRequestHandler<TCommand, Unit>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a command handler that returns a result.
        /// </summary>
        /// <typeparam name="TCommand">The command type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
            where TCommand : ICommand<TResult>
            where THandler : class, ICommandHandler<TCommand, TResult>
        {
            services.AddTransient<IRequestHandler<TCommand, TResult>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a query handler.
        /// </summary>
        /// <typeparam name="TQuery">The query type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <typeparam name="THandler">The handler type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddQueryHandler<TQuery, TResult, THandler>(this IServiceCollection services)
            where TQuery : IQuery<TResult>
            where THandler : class, IQueryHandler<TQuery, TResult>
        {
            services.AddTransient<IRequestHandler<TQuery, TResult>, THandler>();
            return services;
        }

        /// <summary>
        /// Registers a pipeline behavior for a specific request/result pair.
        /// Behaviors execute in registration order (first registered = outermost).
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResult">The result type.</typeparam>
        /// <typeparam name="TBehavior">The behavior type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection for chaining.</returns>
        public static IServiceCollection AddPipelineBehavior<TRequest, TResult, TBehavior>(this IServiceCollection services)
            where TRequest : IRequest<TResult>
            where TBehavior : class, IPipelineBehavior<TRequest, TResult>
        {
            services.AddTransient<IPipelineBehavior<TRequest, TResult>, TBehavior>();
            return services;
        }
    }
}
