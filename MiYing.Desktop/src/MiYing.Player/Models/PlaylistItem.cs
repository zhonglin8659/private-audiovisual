using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MiYing.Player.Models;

/// <summary>本次打开列表中的条目（不持久化）。</summary>
public partial class PlaylistItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(DirectoryName))]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhoto))]
    [NotifyPropertyChangedFor(nameof(KindText))]
    private string _kind = "Video";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isThumbnailLoading;

    [ObservableProperty]
    private string _thumbnailError = string.Empty;

    [ObservableProperty]
    private long _durationMs;

    [ObservableProperty]
    private long _positionMs;

    public bool HasThumbnail => Thumbnail is not null;

    public bool IsPhoto => string.Equals(Kind, "Photo", StringComparison.OrdinalIgnoreCase);

    public string KindText => IsPhoto ? "照片" : "视频";

    public string DirectoryName => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string ResumeText => IsPhoto ? "加密照片" : "视频";
}