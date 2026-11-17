namespace Signal.agent.Models
{
    public class ParamConfig
    {
        public string Command { get; set; } = string.Empty;
        public string Name { get; set; } = null!;
        public string? Uncorrect { get; set; }
        public int PollIntervalSeconds { get; set; } = 30;
        public Dictionary<string, string>? Annotations { get; set; }
    }
}