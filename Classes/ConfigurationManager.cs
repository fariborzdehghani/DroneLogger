using DroneLogger.Model;
using System.IO;
using System.Text.Json;

namespace DroneLogger.Classes
{
    public class ConfigurationManager
    {
        private const string CONFIG_FILE = "droneconfig.json";

        public static void SaveConfiguration(Config config)
        {
            try
            {
                string jsonString = JsonSerializer.Serialize(config, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(CONFIG_FILE, jsonString);
            }
            catch (Exception ex)
            {
                throw new Exception($"Error saving configuration: {ex.Message}");
            }
        }

        public static Config? LoadConfiguration()
        {
            if (!File.Exists(CONFIG_FILE))
            {
                return null;
            }

            try
            {
                string jsonString = File.ReadAllText(CONFIG_FILE);
                return DeserializeConfiguration(jsonString);
            }
            catch
            {
                // If there's any error reading/parsing the file, return null to keep initial values
                return null;
            }
        }

        internal static Config? DeserializeConfiguration(string jsonString)
        {
            Config? config = JsonSerializer.Deserialize<Config>(jsonString);
            if (config == null)
            {
                return null;
            }

            // Migrate configuration files written before the throttle rename.
            using JsonDocument document = JsonDocument.Parse(jsonString);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty(nameof(Config.MinThrottle), out _) &&
                root.TryGetProperty("MinSpeed", out JsonElement oldMin) &&
                oldMin.TryGetInt32(out int minThrottle))
            {
                config.MinThrottle = minThrottle;
            }

            if (!root.TryGetProperty(nameof(Config.MaxThrottle), out _) &&
                root.TryGetProperty("MaxSpeed", out JsonElement oldMax) &&
                oldMax.TryGetInt32(out int maxThrottle))
            {
                config.MaxThrottle = maxThrottle;
            }

            return config;
        }
    }
}
