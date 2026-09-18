namespace Birko.AI.Models
{
    public class ContentBlock
    {
        public string? Type { get; set; }
        public string? Text { get; set; }

        // For tool use
        public string? Id { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, object>? Input { get; set; }
    }
}
