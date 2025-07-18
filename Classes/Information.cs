using System.Text.Json.Serialization;

namespace DroneLogger.Classes
{
    internal class Information
    {
        [JsonPropertyName("Code")]
        public int Code { get; set; }

        [JsonPropertyName("Content")]
        public string Content { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"Information={{\"Code\": {Code}, \"Content\": \"{Content}\"}}";
        }
    }
}
