using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Birko.Framework.Configuration;

namespace Birko.Framework.Services
{
    /// <summary>
    /// Extension methods for configuring Birko Framework services
    /// </summary>
    public static class FrameworkServiceExtensions
    {
        /// <summary>
        /// Adds and configures Birko Framework services to the dependency injection container
        /// </summary>
        public static IServiceCollection AddBirkoFramework(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<FrameworkOptions>? configureOptions = null)
        {
            // Bind configuration
            var frameworkOptions = configuration.GetSection("Framework")
                .Get<FrameworkOptions>() ?? new FrameworkOptions();
            configureOptions?.Invoke(frameworkOptions);

            var dataOptions = configuration.GetSection("Data")
                .Get<DataOptions>() ?? new DataOptions();

            var communicationOptions = configuration.GetSection("Communication")
                .Get<CommunicationOptions>() ?? new CommunicationOptions();

            // Register configuration as singletons
            services.AddSingleton(frameworkOptions);
            services.AddSingleton(dataOptions);
            services.AddSingleton(communicationOptions);

            // Register logging
            services.AddLogging(configure => configure.AddConfiguration(configuration.GetSection("Logging")));

            // Add console and debug logging
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.AddDebug();
            });

            return services;
        }

        /// <summary>
        /// Initializes the framework and performs startup checks
        /// </summary>
        public static async Task<IFrameworkBootstrap> InitializeFrameworkAsync(
            this IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<FrameworkBootstrap>>();
            var frameworkOptions = serviceProvider.GetRequiredService<FrameworkOptions>();

            logger.LogInformation("Initializing {FrameworkName} v{Version}",
                frameworkOptions.Name, frameworkOptions.Version);
            logger.LogInformation("Environment: {Environment}", frameworkOptions.Environment);

            var bootstrap = new FrameworkBootstrap(serviceProvider, logger);

            await bootstrap.InitializeAsync(cancellationToken);

            return bootstrap;
        }
    }

    /// <summary>
    /// Interface for framework bootstrap control
    /// </summary>
    public interface IFrameworkBootstrap
    {
        Task RunAsync(CancellationToken cancellationToken = default);
        Task ShutdownAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Framework bootstrap implementation
    /// </summary>
    internal class FrameworkBootstrap : IFrameworkBootstrap
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<FrameworkBootstrap> _logger;
        private readonly CancellationTokenSource _shutdownCts = new();

        public FrameworkBootstrap(IServiceProvider serviceProvider, ILogger<FrameworkBootstrap> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Framework initialization started");

            // Perform startup checks
            await PerformHealthChecksAsync(cancellationToken);

            _logger.LogInformation("Framework initialized successfully");
        }

        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Framework is running. Press CTRL+C to exit.");

            try
            {
                // Keep application running until shutdown is requested
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Shutdown requested");
            }
        }

        public async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Framework shutdown initiated");

            await PerformCleanupAsync(cancellationToken);

            _logger.LogInformation("Framework shutdown complete");
        }

        private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Performing health checks...");

            // Check if configuration is valid
            var frameworkOptions = _serviceProvider.GetService<FrameworkOptions>();
            if (frameworkOptions == null)
            {
                throw new InvalidOperationException("Framework options not configured");
            }

            _logger.LogDebug("Health checks passed");

            await Task.CompletedTask;
        }

        private async Task PerformCleanupAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Performing cleanup...");

            // Cleanup resources here
            _shutdownCts.Cancel();
            _shutdownCts.Dispose();

            await Task.CompletedTask;
        }
    }
}
