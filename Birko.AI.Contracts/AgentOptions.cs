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
                CheckpointInterval = CheckpointInterval
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
        }

        public static AgentOptions FromDictionary(Dictionary<string, string> config)
        {
            var options = new AgentOptions();

            if (config.TryGetValue("interactive", out var interactive))
                options.Interactive = bool.Parse(interactive);
            if (config.TryGetValue("maxIterations", out var maxIterations))
                options.MaxIterations = int.Parse(maxIterations);
            if (config.TryGetValue("maxIterationsPerStep", out var maxIterationsPerStep))
                options.MaxIterationsPerStep = int.Parse(maxIterationsPerStep);
            if (config.TryGetValue("verbose", out var verbose))
                options.Verbose = bool.Parse(verbose);
            if (config.TryGetValue("workingDirectory", out var workingDirectory))
                options.WorkingDirectory = workingDirectory;
            if (config.TryGetValue("promptTimeout", out var promptTimeout))
                options.PromptTimeout = int.Parse(promptTimeout);
            if (config.TryGetValue("defaultPromptResponse", out var defaultPromptResponse))
                options.DefaultPromptResponse = defaultPromptResponse;
            if (config.TryGetValue("modelDepth", out var modelDepth))
                options.ModelDepth = int.Parse(modelDepth);

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
                ["modelDepth"] = ModelDepth.ToString()
            };

            if (DefaultPromptResponse != null)
                dict["defaultPromptResponse"] = DefaultPromptResponse;

            return dict;
        }
    }
}
