using System.IO;
using System.Text.Json;

namespace GeometryGrid.App.Services;

public sealed class AppPreferences
{
    private static readonly object SyncRoot = new();
    private static AppPreferences _current = LoadCore(SettingsPath);

    public bool ExperimentalGpuAcceleration { get; set; }
    public bool LiquidGlassEnabled { get; set; }

    public static AppPreferences Current
    {
        get
        {
            lock (SyncRoot)
                return _current;
        }
    }

    public static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GeometryGrid",
            "settings.json");

    public static void Save(
        bool experimentalGpuAcceleration,
        bool liquidGlassEnabled)
    {
        lock (SyncRoot)
        {
            _current.ExperimentalGpuAcceleration = experimentalGpuAcceleration;
            _current.LiquidGlassEnabled = liquidGlassEnabled;
            SaveCore(SettingsPath, _current);
        }
    }

    internal static AppPreferences LoadCore(string path)
    {
        try
        {
            if (!File.Exists(path))
                return new AppPreferences();
            return JsonSerializer.Deserialize<AppPreferences>(
                       File.ReadAllText(path))
                   ?? new AppPreferences();
        }
        catch
        {
            return new AppPreferences();
        }
    }

    internal static void SaveCore(string path, AppPreferences preferences)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                preferences,
                new JsonSerializerOptions { WriteIndented = true }));
    }
}
