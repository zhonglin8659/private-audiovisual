using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using MiYing.Player.ViewModels;

namespace MiYing.Player.Models;

/// <summary>最近播放记录（保存在本地，仅包含文件路径与播放进度）。</summary>
public partial class RecentItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(DirectoryName))]
    private string _path = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumeText))]
    private long _positionMs;

    [ObservableProperty]
    private long _durationMs;

    [ObservableProperty]
    private DateTime _lastPlayedUtc;

    /// <summary>"Video" 或 "Photo"；旧记录没有该字段时默认视频。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResumeText))]
    [NotifyPropertyChangedFor(nameof(IsPhoto))]
    private string _kind = "Video";

    [JsonIgnore]
    public bool IsPhoto => string.Equals(Kind, "Photo", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);

    [JsonIgnore]
    public string DirectoryName => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    [JsonIgnore]
    public string ResumeText => IsPhoto
        ? "加密照片"
        : PositionMs > 15_000
            ? $"上次看到 {PlayerViewModel.FormatTime(PositionMs)}"
            : "尚未观看";
}