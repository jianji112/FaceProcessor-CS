using System;
using System.IO;
using System.Text.Json;
using FaceProcessor.Models;

namespace FaceProcessor.Services;

public class ConfigManager
{
	private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

	public AppConfig Config { get; private set; }

	public ConfigManager()
	{
		Config = Load();
	}

	public AppConfig Load()
	{
		if (!File.Exists(ConfigPath))
		{
			return new AppConfig();
		}
		try
		{
			return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig();
		}
		catch
		{
			return new AppConfig();
		}
	}

	public void Save()
	{
		string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions
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
