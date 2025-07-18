using System.Text.Json.Serialization;

namespace DroneLogger.Classes
{
    internal class Error
    {
        [JsonPropertyName("Code")]
        public int Code { get; set; }

        [JsonPropertyName("Content")]
        public string Content { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"Error={{\"Code\": {Code}, \"Content\": \"{Content}\"}}";
        }
    }
}
