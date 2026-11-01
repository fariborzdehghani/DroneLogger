using DroneLogger.Model;
using System.IO;
using System.Text.Json;

namespace DroneLogger.Classes;

public sealed class CameraViewState
{
    public double Azimuth { get; set; }
    public double Elevation { get; set; }
    public double Distance { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double TargetZ { get; set; }
    public bool IsValid => new[] { Azimuth, Elevation, Distance, TargetX, TargetY, TargetZ }.All(double.IsFinite)
        && Distance >= 0.35 && Distance <= 8 && Elevation >= -85 && Elevation <= 85;
}

internal sealed class AppViewState
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DroneLogger", "view-state.json");
    public Dictionary<string, CameraViewState> Cameras { get; set; } = new();
    public Config? Configuration { get; set; }
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 1858;
    public double Height { get; set; } = 953;
    public bool Maximized { get; set; } = true;
    public int MainTab { get; set; }
    public int ReviewTab { get; set; }
    public int ConnectionType { get; set; }
    public bool GraphsVisible { get; set; } = true;
    public double GraphsWidth { get; set; }
    public string? FlightLogPath { get; set; }
    public double ReviewPositionMs { get; set; }

    public static AppViewState Load(string? path = null)
    {
        try { return JsonSerializer.Deserialize<AppViewState>(File.ReadAllText(path ?? FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }
}
