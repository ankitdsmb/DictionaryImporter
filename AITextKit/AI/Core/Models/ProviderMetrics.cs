namespace DictionaryImporter.AITextKit.AI.Core.Models
{
    public class ProviderMetrics
    {
        public string ProviderName { get; set; }

        public DateTime MetricDate { get; set; }

        public int TotalRequests { get; set; }

        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }

        public long TotalTokens { get; set; }

        public long TotalDurationMs { get; set; }
        public decimal TotalCost { get; set; }

        public Dictionary<string, int> ErrorCounts { get; set; } = new();

        public Dictionary<string, Dictionary<string, int>> ErrorBreakdown { get; set; } = new();

        public DateTime LastUsed { get; set; }

        public bool IsHealthy { get; set; }

        public double SuccessRate => TotalRequests > 0 ? SuccessfulRequests * 100.0 / TotalRequests : 0;

        public double AverageResponseTimeMs { get; set; }

        public double CalculatedAverageResponseTimeMs => TotalRequests > 0 ? (double)TotalDurationMs / TotalRequests : 0;

        public double AverageTokensPerRequest => TotalRequests > 0 ? (double)TotalTokens / TotalRequests : 0;
        public decimal AverageCostPerRequest => TotalRequests > 0 ? TotalCost / TotalRequests : 0;

        public string Name
        {
            get => ProviderName;
            set => ProviderName = value;
        }

        public long TokensUsed
        {
            get => TotalTokens;
            set => TotalTokens = value;
        }

        public long DurationMs
        {
            get => TotalDurationMs;
            set => TotalDurationMs = value;
        }

        public decimal EstimatedCost
        {
            get => TotalCost;
            set => TotalCost = value;
        }
    }
}