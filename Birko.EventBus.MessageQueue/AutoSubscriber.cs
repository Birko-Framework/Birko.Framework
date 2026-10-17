using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.EventBus.MessageQueue
{
    /// <summary>
    /// Scans DI registrations for <see cref="IEventHandler{TEvent}"/> implementations
    /// and creates transport-level subscriptions for each event type.
    /// </summary>
    public class AutoSubscriber
    {
        private readonly DistributedEventBus _bus;
        private readonly IServiceProvider _serviceProvider;

        public AutoSubscriber(DistributedEventBus bus, IServiceProvider serviceProvider)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        /// <summary>
        /// Discovers all IEventHandler&lt;T&gt; registrations and subscribes to their transport topics.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task SubscribeAllAsync(CancellationToken cancellationToken = default)
        {
            var eventTypes = DiscoverEventTypes();

            foreach (var eventType in eventTypes)
            {
                // Call SubscribeToTransportAsync<TEvent> via reflection
                var method = typeof(DistributedEventBus)
                    .GetMethod(nameof(DistributedEventBus.SubscribeToTransportAsync))!
                    .MakeGenericMethod(eventType);

                var task = (Task)method.Invoke(_bus, [cancellationToken])!;
                await task.ConfigureAwait(false);
            }
        }

        private HashSet<Type> DiscoverEventTypes()
        {
            var eventTypes = new HashSet<Type>();

            // Scan all loaded assemblies for IEventHandler<T> implementations
            // that are registered in DI
            var handlerOpenType = typeof(IEventHandler<>);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray()!;
                }

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface)
                    {
                        continue;
                    }

                    foreach (var iface in type.GetInterfaces())
                    {
                        if (iface.IsGenericType && iface.GetGenericTypeDefinition() == handlerOpenType)
                        {
                            var eventType = iface.GetGenericArguments()[0];
                            // CR-L257: align the discovery filter with SubscribeToTransportAsync<TEvent>'s
                            // `class, IEvent` constraint before MakeGenericMethod(eventType), so a bad type
                            // is skipped rather than throwing an ArgumentException at reflection time. The
                            // IEvent check is defensive: IEventHandler<T> already constrains `T : IEvent`, so
                            // a closed handler type can't carry a non-IEvent T today — but it makes the guard
                            // robust if that interface constraint ever loosens, and documents the requirement.
                            if (eventType.IsClass && typeof(IEvent).IsAssignableFrom(eventType))
                            {
                                if (IsRegistered(handlerOpenType.MakeGenericType(eventType)))
                                {
                                    eventTypes.Add(eventType);
                                }
                            }
                        }
                    }
                }
            }

            return eventTypes;
        }

        /// <summary>
        /// TASK-566: whether DI has a registration for <paramref name="handlerType"/>, asked without resolving it from the
        /// root, where a handler with a scoped dependency throws under ValidateScopes before any message is consumed.
        /// The container answers through <see cref="IServiceProviderIsService"/> without building a handler. A container
        /// that does not offer it is asked inside a short-lived scope, which does build the handlers, as before.
        /// </summary>
        private bool IsRegistered(Type handlerType)
        {
            if (_serviceProvider.GetService(typeof(IServiceProviderIsService)) is IServiceProviderIsService isService)
            {
                return isService.IsService(handlerType);
            }

            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetService(typeof(IEnumerable<>).MakeGenericType(handlerType));
            return handlers is System.Collections.IEnumerable enumerable && enumerable.Cast<object>().Any();
        }
    }
}
