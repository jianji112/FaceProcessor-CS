using System.Text.Json;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

/// <summary>配置管理</summary>
public class ConfigManager
{
    private static readonly string ConfigPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "config.json");

    public AppConfig Config { get; private set; }

    public ConfigManager()
    {
        Config = Load();
    }

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(ConfigPath, json);
    }

    public void Update(Action<AppConfig> action)
    {
        action(Config);
        Save();
    }
}
