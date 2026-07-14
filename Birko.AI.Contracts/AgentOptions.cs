namespace Birko.AI
{
    /// <summary>
    /// Configuration options for agent behavior.
    /// </summary>
    public class AgentOptions
    {
        /// <summary>
        /// Enable/disable interactive mode (user prompts). Default: true
        /// </summary>
        public bool Interactive { get; set; } = true;

        /// <summary>
        /// Maximum number of iterations for agent execution. Default: 10
        /// </summary>
        public int MaxIterations { get; set; } = 10;

        /// <summary>
        /// Maximum iterations allowed per plan step. Prevents one step from consuming entire budget.
        /// Default: 10
        /// </summary>
        public int MaxIterationsPerStep { get; set; } = 10;

        /// <summary>
        /// Enable verbose logging output. Default: true
        /// </summary>
        public bool Verbose { get; set; } = true;

        /// <summary>
        /// Working directory for agent operations.
        /// </summary>
        public string WorkingDirectory { get; set; } = "./";

        /// <summary>
        /// Timeout for interactive prompts in seconds. Default: 300 (5 minutes)
        /// </summary>
        public int PromptTimeout { get; set; } = 300;

        /// <summary>
        /// Default response for prompts in non-interactive mode.
        /// If null or empty, prompts will return an error in non-interactive mode.
        /// </summary>
        public string? DefaultPromptResponse { get; set; }

        /// <summary>
        /// Model thinking/reasoning depth level. Higher values encourage deeper reasoning.
        /// Range: 0-10, Default: 5
        /// </summary>
        public int ModelDepth { get; set; } = 5;

        /// <summary>
        /// External paths (outside workspace) that are allowed for file operations.
        /// </summary>
        public List<string> AllowedExternalPaths { get; set; } = new();

        /// <summary>
        /// Enable streaming mode for LLM responses. Default: false
        /// </summary>
        public bool EnableStreaming { get; set; } = false;

        /// <summary>
        /// Fallback to synchronous mode if streaming fails. Default: true
        /// </summary>
        public bool StreamingFallbackToSync { get; set; } = true;

        /// <summary>
        /// Interval (in iterations) between self-reflection checkpoints. Default: 3
        /// Set to 0 to disable checkpoint prompts.
        /// </summary>
        public int CheckpointInterval { get; set; } = 3;

        /// <summary>
        /// Optional callback invoked after each successful LLM response.
        /// </summary>
        public Action? OnLlmResponseReceived { get; set; }

        public AgentOptions Clone()
        {
            return new AgentOptions
            {
                Interactive = Interactive,
                MaxIterations = MaxIterations,
                MaxIterationsPerStep = MaxIterationsPerStep,
                Verbose = Verbose,
                WorkingDirectory = WorkingDirectory,
                PromptTimeout = PromptTimeout,
                DefaultPromptResponse = DefaultPromptResponse,
                ModelDepth = ModelDepth,
                AllowedExternalPaths = new List<string>(AllowedExternalPaths),
                EnableStreaming = EnableStreaming,
                StreamingFallbackToSync = StreamingFallbackToSync,
                CheckpointInterval = CheckpointInterval,
                OnLlmResponseReceived = OnLlmResponseReceived
            };
        }

        public void Merge(AgentOptions other)
        {
            if (other == null) return;

            Interactive = other.Interactive;
            MaxIterations = other.MaxIterations;
            MaxIterationsPerStep = other.MaxIterationsPerStep;
            Verbose = other.Verbose;
            if (!string.IsNullOrEmpty(other.WorkingDirectory))
                WorkingDirectory = other.WorkingDirectory;
            PromptTimeout = other.PromptTimeout;
            if (other.DefaultPromptResponse != null)
                DefaultPromptResponse = other.DefaultPromptResponse;
            ModelDepth = other.ModelDepth;
            if (other.AllowedExternalPaths.Count > 0)
                AllowedExternalPaths = new List<string>(other.AllowedExternalPaths);
            EnableStreaming = other.EnableStreaming;
            StreamingFallbackToSync = other.StreamingFallbackToSync;
            CheckpointInterval = other.CheckpointInterval;
            if (other.OnLlmResponseReceived != null)
                OnLlmResponseReceived = other.OnLlmResponseReceived;
        }

        public static AgentOptions FromDictionary(Dictionary<string, string> config)
        {
            var options = new AgentOptions();

            // Use TryParse throughout so a malformed config value (e.g. maxIterations="ten")
            // is skipped rather than throwing FormatException out of this otherwise-tolerant
            // factory (it already skips absent keys) (CR-L005).
            if (config.TryGetValue("interactive", out var interactive) && bool.TryParse(interactive, out var interactiveVal))
                options.Interactive = interactiveVal;
            if (config.TryGetValue("maxIterations", out var maxIterations) && int.TryParse(maxIterations, out var maxIterationsVal))
                options.MaxIterations = maxIterationsVal;
            if (config.TryGetValue("maxIterationsPerStep", out var maxIterationsPerStep) && int.TryParse(maxIterationsPerStep, out var maxIterationsPerStepVal))
                options.MaxIterationsPerStep = maxIterationsPerStepVal;
            if (config.TryGetValue("verbose", out var verbose) && bool.TryParse(verbose, out var verboseVal))
                options.Verbose = verboseVal;
            if (config.TryGetValue("workingDirectory", out var workingDirectory))
                options.WorkingDirectory = workingDirectory;
            if (config.TryGetValue("promptTimeout", out var promptTimeout) && int.TryParse(promptTimeout, out var promptTimeoutVal))
                options.PromptTimeout = promptTimeoutVal;
            if (config.TryGetValue("defaultPromptResponse", out var defaultPromptResponse))
                options.DefaultPromptResponse = defaultPromptResponse;
            if (config.TryGetValue("modelDepth", out var modelDepth) && int.TryParse(modelDepth, out var modelDepthVal))
                options.ModelDepth = modelDepthVal;
            if (config.TryGetValue("allowedExternalPaths", out var allowedExternalPaths))
                options.AllowedExternalPaths = allowedExternalPaths.Length == 0
                    ? new List<string>()
                    : new List<string>(allowedExternalPaths.Split('\n'));
            if (config.TryGetValue("enableStreaming", out var enableStreaming) && bool.TryParse(enableStreaming, out var enableStreamingVal))
                options.EnableStreaming = enableStreamingVal;
            if (config.TryGetValue("streamingFallbackToSync", out var streamingFallbackToSync) && bool.TryParse(streamingFallbackToSync, out var streamingFallbackToSyncVal))
                options.StreamingFallbackToSync = streamingFallbackToSyncVal;
            if (config.TryGetValue("checkpointInterval", out var checkpointInterval) && int.TryParse(checkpointInterval, out var checkpointIntervalVal))
                options.CheckpointInterval = checkpointIntervalVal;

            return options;
        }

        public Dictionary<string, string> ToDictionary()
        {
            var dict = new Dictionary<string, string>
            {
                ["interactive"] = Interactive.ToString(),
                ["maxIterations"] = MaxIterations.ToString(),
                ["maxIterationsPerStep"] = MaxIterationsPerStep.ToString(),
                ["verbose"] = Verbose.ToString(),
                ["workingDirectory"] = WorkingDirectory,
                ["promptTimeout"] = PromptTimeout.ToString(),
                ["modelDepth"] = ModelDepth.ToString(),
                ["allowedExternalPaths"] = string.Join('\n', AllowedExternalPaths),
                ["enableStreaming"] = EnableStreaming.ToString(),
                ["streamingFallbackToSync"] = StreamingFallbackToSync.ToString(),
                ["checkpointInterval"] = CheckpointInterval.ToString()
            };

            if (DefaultPromptResponse != null)
                dict["defaultPromptResponse"] = DefaultPromptResponse;

            return dict;
        }
    }
}
