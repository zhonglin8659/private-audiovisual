using System.IO;
using System.Text.Json;

namespace MiYing.Encryptor.Services;

/// <summary>主题模式。</summary>
public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>简单的本地设置存储。</summary>
public sealed class AppSettings
{
    public AppThemeMode Theme { get; set; } = AppThemeMode.System;

    private static string SettingsPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MiYing");
            return Path.Combine(dir, "encryptor.settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
        }
        catch
        {
            // 设置损坏时回退到默认
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // 写入失败不影响主流程
        }
    }
}