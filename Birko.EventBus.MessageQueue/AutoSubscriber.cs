using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

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
                            if (eventType.IsClass)
                            {
                                // Verify it's actually registered in DI
                                var handlerServiceType = typeof(IEnumerable<>).MakeGenericType(
                                    handlerOpenType.MakeGenericType(eventType));
                                var handlers = _serviceProvider.GetService(handlerServiceType);
                                if (handlers is System.Collections.IEnumerable enumerable && enumerable.Cast<object>().Any())
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
    }
}
