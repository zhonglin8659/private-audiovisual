using System.IO;
using System.Text.Json;
using MiYing.Player.Models;

namespace MiYing.Player.Services;

/// <summary>播放器偏好设置。</summary>
public sealed class PlayerSettings
{
    public bool RememberPosition { get; set; } = true;

    public bool TrackRecent { get; set; } = true;

    public int Volume { get; set; } = 80;
}

/// <summary>持久化状态。</summary>
public sealed class PlayerState
{
    public List<RecentItem> Recent { get; set; } = new();

    public PlayerSettings Settings { get; set; } = new();
}

/// <summary>本地状态存储（%APPDATA%\MiYing\player.state.json）。</summary>
public sealed class RecentStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public PlayerState State { get; private set; } = new();

    private static string StatePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MiYing");
            return Path.Combine(dir, "player.state.json");
        }
    }

    public static RecentStore Load()
    {
        var store = new RecentStore();
        try
        {
            if (File.Exists(StatePath))
            {
                store.State = JsonSerializer.Deserialize<PlayerState>(File.ReadAllText(StatePath), Options) ?? new PlayerState();
            }
        }
        catch
        {
            store.State = new PlayerState();
        }

        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(State, Options));
        }
        catch
        {
            // 保存失败不影响播放
        }
    }
}