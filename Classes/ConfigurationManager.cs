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
                return JsonSerializer.Deserialize<Config>(jsonString);
            }
            catch
            {
                // If there's any error reading/parsing the file, return null to keep initial values
                return null;
            }
        }
    }
}