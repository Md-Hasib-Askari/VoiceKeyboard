using System;
using System.IO;
using System.Text.Json;

namespace VoiceKeyboard.Services;

public class AppSettings
{
    public string PythonPath { get; set; } = "python3";
}

public static class ConfigService
{
    private static readonly string ConfigDir =
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "voice-keyboard", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
