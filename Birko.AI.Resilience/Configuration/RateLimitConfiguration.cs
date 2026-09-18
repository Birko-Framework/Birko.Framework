namespace Birko.AI.Resilience.Configuration
{
    public class RateLimitConfiguration
    {
        public bool Enabled { get; set; } = false;
        public List<ProviderRateLimit> ProviderLimits { get; set; } = new();
    }

    public class ProviderRateLimit
    {
        public string Provider { get; set; } = "";
        public int RequestsPerMinute { get; set; } = 60;
        public int TokensPerMinute { get; set; } = 0;
        public int RequestsPerDay { get; set; } = 0;
        public int TokensPerDay { get; set; } = 0;
    }
}
