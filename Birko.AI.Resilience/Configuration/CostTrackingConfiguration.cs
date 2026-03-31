namespace Birko.AI.Resilience.Configuration
{
    public class CostTrackingConfiguration
    {
        public bool Enabled { get; set; } = true;
        public List<ProviderPricing> Pricing { get; set; } = new();
        public BudgetConfiguration Budget { get; set; } = new();
    }

    public class ProviderPricing
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "*";
        public double InputPricePerMillionTokens { get; set; }
        public double OutputPricePerMillionTokens { get; set; }
    }

    public class BudgetConfiguration
    {
        public double DailyBudgetUsd { get; set; } = 0;
        public double MonthlyBudgetUsd { get; set; } = 0;
        public double ProjectBudgetUsd { get; set; } = 0;
        public double WarningThresholdPercent { get; set; } = 80;
    }
}
