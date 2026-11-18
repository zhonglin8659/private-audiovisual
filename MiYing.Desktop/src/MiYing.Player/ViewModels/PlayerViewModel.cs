using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using MiYing.Core.Crypto;
using MiYing.Core.Naming;
using MiYing.Player.Models;
using MiYing.Player.Services;
using MiYing.UI;
using Microsoft.Win32;

namespace MiYing.Player.ViewModels;

public enum PlayerViewState
{
    Home,
    Unlock,
    Playing,
    ViewingPhoto,
}

/// <summary>音轨 / 字幕轨条目。</summary>
public partial class TrackItem : ObservableObject
{
    public TrackItem(int id, string name, bool isSelected)
    {
        Id = id;
        Name = name;
        IsSelected = isSelected;
    }

    public int Id { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>倍速选项。</summary>
public partial class SpeedOption : ObservableObject
{
    public SpeedOption(double value)
    {
        Value = value;
    }

    public double Value { get; }

    public string Label => Value == 1.0 ? "正常" : $"{Value.ToString("0.##", CultureInfo.InvariantCulture)}×";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>播放器主逻辑。</summary>
public partial class PlayerViewModel : ObservableObject
{
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current.Dispatcher;
    private readonly RecentStore _store = RecentStore.Load();
    private readonly LibVLC _libVlc;

    private Media? _media;
    private EncryptedVideoStream? _contentStream;
    private string? _sessionPassword;
    private long? _pendingResumeMs;
    private DateTime _lastPositionSave = DateTime.MinValue;
    private readonly SemaphoreSlim _thumbnailSemaphore = new(2);
    private int _thumbnailGeneration;

    public MediaPlayer MediaPlayer { get; }

    public PlayerSettings Settings => _store.State.Settings;

    public ObservableCollection<RecentItem> Recent { get; } = new();

    /// <summary>本次运行中手动打开的文件列表（不持久化）。</summary>
    public ObservableCollection<PlaylistItem> Playlist { get; } = new();

    public bool HasPlaylist => Playlist.Count > 0;

    public string PlaylistSummary => $"{Playlist.Count} 个文件";

    public int CurrentPlaylistIndex
    {
        get
        {
            if (string.IsNullOrEmpty(CurrentPath))
            {
                return -1;
            }

            return Playlist.ToList().FindIndex(item =>
                string.Equals(item.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool HasPrevious => CurrentPlaylistIndex > 0;

    public bool HasNext
    {
        get
        {
            int index = CurrentPlaylistIndex;
            return index >= 0 && index < Playlist.Count - 1;
        }
    }

    public ObservableCollection<TrackItem> AudioTracks { get; } = new();

    public ObservableCollection<TrackItem> SubtitleTracks { get; } = new();

    public ObservableCollection<SpeedOption> SpeedOptions { get; } = new();

    // ---------- 状态 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeVisible))]
    [NotifyPropertyChangedFor(nameof(IsUnlockVisible))]
    [NotifyPropertyChangedFor(nameof(IsPlayingVisible))]
    [NotifyPropertyChangedFor(nameof(IsViewingPhoto))]
    [NotifyPropertyChangedFor(nameof(IsPausedOverlayVisible))]
    private PlayerViewState _state = PlayerViewState.Home;

    public bool IsHomeVisible => State == PlayerViewState.Home;

    public bool IsUnlockVisible => State == PlayerViewState.Unlock;

    public bool IsPlayingVisible => State == PlayerViewState.Playing;

    public bool IsViewingPhoto => State == PlayerViewState.ViewingPhoto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPausedOverlayVisible))]
    private bool _isPlaying;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPausedOverlayVisible))]
    private bool _isEnded;

    public bool IsPausedOverlayVisible => State == PlayerViewState.Playing && (!IsPlaying || IsEnded);

    [ObservableProperty]
    private string _windowTitle = "密影播放器";

    // ---------- 当前文件 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnlockTitle))]
    [NotifyPropertyChangedFor(nameof(UnlockButtonText))]
    private MediaKind _currentKind = MediaKind.Video;

    public string UnlockTitle => CurrentKind switch
    {
        MediaKind.Photo => "输入密码解锁查看",
        MediaKind.Unknown => "输入密码解锁",
        _ => "输入密码解锁播放",
    };

    public string UnlockButtonText => CurrentKind switch
    {
        MediaKind.Photo => "解锁并查看",
        MediaKind.Unknown => "解锁",
        _ => "解锁并播放",
    };

    [ObservableProperty]
    private System.Windows.Media.ImageSource? _photoSource;

    [ObservableProperty]
    private string _photoInfoText = string.Empty;

    [ObservableProperty]
    private int _photoPixelWidth;

    [ObservableProperty]
    private int _photoPixelHeight;

    [ObservableProperty]
    private string? _currentPath;

    partial void OnCurrentPathChanged(string? value) => UpdatePlaylistNavigation();

    [ObservableProperty]
    private string _currentFileName = string.Empty;

    [ObservableProperty]
    private string _fileSizeText = string.Empty;

    [ObservableProperty]
    private string _unlockPassword = string.Empty;

    [ObservableProperty]
    private string _unlockError = string.Empty;

    [ObservableProperty]
    private bool _isUnlockBusy;

    [ObservableProperty]
    private bool _rememberSessionPassword;

    public bool HasSessionPassword => !string.IsNullOrEmpty(_sessionPassword);

    // ---------- 首页提示 ----------

    [ObservableProperty]
    private string _homeError = string.Empty;

    [ObservableProperty]
    private bool _isHomeErrorVisible;

    // ---------- 播放进度 ----------

    [ObservableProperty]
    private double _positionMs;

    [ObservableProperty]
    private double _durationMs;

    [ObservableProperty]
    private double _volume = 80;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private string _currentRateText = "1×";

    [ObservableProperty]
    private bool _isTopmost;

    [ObservableProperty]
    private bool _isSeeking;

    public bool IsValidDuration => DurationMs > 0;

    // ---------- 菜单 ----------

    [ObservableProperty]
    private bool _isSpeedMenuOpen;

    [ObservableProperty]
    private bool _isAudioMenuOpen;

    [ObservableProperty]
    private bool _isSubtitleMenuOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    // ---------- 恢复播放提示 ----------

    [ObservableProperty]
    private bool _isResumeNoticeVisible;

    [ObservableProperty]
    private string _resumeNoticeText = string.Empty;

    public PlayerViewModel()
    {
        _libVlc = new LibVLC("--no-video-title-show");
        MediaPlayer = new MediaPlayer(_libVlc)
        {
            Volume = Math.Clamp(Settings.Volume, 0, 100),
        };

        Volume = MediaPlayer.Volume;

        MediaPlayer.TimeChanged += OnTimeChanged;
        MediaPlayer.LengthChanged += OnLengthChanged;
        MediaPlayer.Playing += OnPlaying;
        MediaPlayer.Paused += OnPaused;
        MediaPlayer.Stopped += OnStopped;
        MediaPlayer.EndReached += OnEndReached;
        MediaPlayer.EncounteredError += OnEncounteredError;
        MediaPlayer.Muted += (_, _) => _dispatcher.InvokeAsync(() => IsMuted = true);
        MediaPlayer.Unmuted += (_, _) => _dispatcher.InvokeAsync(() => IsMuted = false);

        foreach (RecentItem item in _store.State.Recent)
        {
            Recent.Add(item);
        }

        foreach (double rate in new[] { 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 })
        {
            SpeedOptions.Add(new SpeedOption(rate));
        }

        SpeedOptions.First(o => o.Value == 1.0).IsSelected = true;

        Playlist.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPlaylist));
            OnPropertyChanged(nameof(PlaylistSummary));
            UpdatePlaylistNavigation();
        };
    }

    // ---------- 打开 / 解锁 ----------

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开密影加密文件（可多选）",
            Filter = "密影加密文件 (*.msv;*.msp)|*.msv;*.msp|视频 (*.msv)|*.msv|照片 (*.msp)|*.msp|所有文件|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true)
        {
            await OpenFilesAsync(dialog.FileNames, replacePlaylist: true);
        }
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "添加密影加密文件（可多选）",
            Filter = "密影加密文件 (*.msv;*.msp)|*.msv;*.msp|视频 (*.msv)|*.msv|照片 (*.msp)|*.msp|所有文件|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() == true)
        {
            await OpenFilesAsync(dialog.FileNames, replacePlaylist: false);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentItem? item)
    {
        if (item is not null)
        {
            await OpenPathAsync(item.Path);
        }
    }

    /// <summary>批量打开文件。多选时只建立列表，点击列表项后再解锁播放/查看。</summary>
    public async Task OpenFilesAsync(IEnumerable<string> paths, bool replacePlaylist)
    {
        IsSettingsOpen = false;
        IsHomeErrorVisible = false;
        CloseMenus();

        if (replacePlaylist)
        {
            CloseMedia();
            _thumbnailGeneration++;
            Playlist.Clear();
        }

        int added = 0;
        foreach (string path in paths
                     .Where(p => p.EndsWith(".msv", StringComparison.OrdinalIgnoreCase)
                                 || p.EndsWith(".msp", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string full = Path.GetFullPath(path);
                if (!File.Exists(full) || Playlist.Any(item => string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                Playlist.Add(CreatePlaylistItem(full));
                added++;
            }
            catch
            {
                // 单个文件读取失败不影响其他文件入列。
            }
        }

        UpdatePlaylistNavigation();

        if (replacePlaylist && Playlist.Count == 1 && added == 1)
        {
            await OpenPathAsync(Playlist[0].Path);
            return;
        }

        if (replacePlaylist)
        {
            State = PlayerViewState.Home;
            WindowTitle = "密影播放器";
        }

        if (HasSessionPassword)
        {
            _ = LoadPlaylistThumbnailsAsync(_sessionPassword!);
        }
    }

    private PlaylistItem CreatePlaylistItem(string fullPath)
    {
        MediaKind kind = fullPath.EndsWith(".msp", StringComparison.OrdinalIgnoreCase)
            ? MediaKind.Photo
            : MediaKind.Video;
        string displayName = Path.GetFileNameWithoutExtension(fullPath);

        try
        {
            MsvHeaderInfo info = MsvVideo.ReadHeader(fullPath);
            kind = MsvNaming.DetectMediaKind(info.Header.OriginalFileName, fullPath);
            if (!string.IsNullOrWhiteSpace(info.Header.OriginalFileName))
            {
                displayName = Path.GetFileNameWithoutExtension(info.Header.OriginalFileName);
            }
        }
        catch
        {
            // 文件头读取失败时仍保留扩展名判断，点击时再提示具体错误。
        }

        return new PlaylistItem
        {
            Path = fullPath,
            DisplayName = displayName,
            Kind = kind == MediaKind.Photo ? "Photo" : "Video",
        };
    }

    public async Task OpenPathAsync(string path)
    {
        ReleaseMedia();
        IsSettingsOpen = false;
        IsHomeErrorVisible = false;
        CloseMenus();

        try
        {
            string full = Path.GetFullPath(path);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("文件不存在或已被移动。");
            }

            MsvHeaderInfo info = await Task.Run(() => MsvVideo.ReadHeader(full));

            CurrentKind = MsvNaming.DetectMediaKind(info.Header.OriginalFileName, full);
            CurrentPath = full;
            CurrentFileName = string.IsNullOrEmpty(info.Header.OriginalFileName)
                ? Path.GetFileName(full)
                : info.Header.OriginalFileName;
            PlaylistItem? playlistItem = Playlist.FirstOrDefault(item =>
                string.Equals(item.Path, full, StringComparison.OrdinalIgnoreCase));
            if (playlistItem is null)
            {
                playlistItem = CreatePlaylistItem(full);
                Playlist.Add(playlistItem);
            }

            playlistItem.DisplayName = Path.GetFileNameWithoutExtension(CurrentFileName);
            playlistItem.Kind = CurrentKind == MediaKind.Photo ? "Photo" : "Video";
            playlistItem.IsActive = true;
            NotifyPlaylistNavigation();
            FileSizeText = FormatSize(new FileInfo(full).Length);
            WindowTitle = CurrentFileName + " — 密影播放器";

            UnlockError = string.Empty;
            UnlockPassword = HasSessionPassword ? _sessionPassword! : string.Empty;
            State = PlayerViewState.Unlock;
            _pendingResumeMs = null;

            AddToRecent(full, CurrentKind);

            if (HasSessionPassword)
            {
                await TryUnlockAsync();
            }
        }
        catch (Exception ex)
        {
            HomeError = ex.Message;
            IsHomeErrorVisible = true;
            State = PlayerViewState.Home;
        }
    }

    [RelayCommand]
    private async Task UnlockAsync() => await TryUnlockAsync();

    /// <summary>使用指定密码解锁（供自检 / 自动化使用）。</summary>
    public async Task UnlockWithPasswordAsync(string password)
    {
        UnlockPassword = password;
        await TryUnlockAsync();
    }

    private async Task TryUnlockAsync()
    {
        if (IsUnlockBusy || string.IsNullOrEmpty(CurrentPath))
        {
            return;
        }

        if (string.IsNullOrEmpty(UnlockPassword))
        {
            UnlockError = "请输入密码。";
            return;
        }

        IsUnlockBusy = true;
        UnlockError = string.Empty;

        try
        {
            string path = CurrentPath;
            string password = UnlockPassword;

            if (CurrentKind == MediaKind.Unknown)
            {
                CurrentKind = await Task.Run(() => MsvVideo.DetectMediaKindByContent(path, password));
                UpdateRecentKind(path, CurrentKind);
            }

            if (CurrentKind == MediaKind.Photo)
            {
                PhotoLoadResult photo = await Task.Run(() =>
                {
                    using EncryptedVideoStream stream = MsvVideo.OpenPlaybackStream(path, password);
                    return PhotoImageLoader.Load(stream, 6000);
                });
                PlaylistItem? photoItem = Playlist.FirstOrDefault(item =>
                    string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (photoItem is not null)
                {
                    photoItem.Kind = "Photo";
                    photoItem.Thumbnail = photo.Image;
                }

                _ = LoadPlaylistThumbnailsAsync(password);
                if (RememberSessionPassword)
                {
                    _sessionPassword = password;
                    OnPropertyChanged(nameof(HasSessionPassword));
                }

                _contentStream = null;
                PhotoSource = photo.Image;
                PhotoPixelWidth = photo.PixelWidth;
                PhotoPixelHeight = photo.PixelHeight;
                PhotoInfoText = BuildPhotoInfo(photo);

                IsPlaying = false;
                IsEnded = false;
                PositionMs = 0;
                DurationMs = 0;
                State = PlayerViewState.ViewingPhoto;
            }
            else
            {
                EncryptedVideoStream stream = await Task.Run(() => MsvVideo.OpenPlaybackStream(path, password));
                PlaylistItem? videoItem = Playlist.FirstOrDefault(item =>
                    string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
                if (videoItem is not null)
                {
                    videoItem.Kind = "Video";
                }

                _ = LoadPlaylistThumbnailsAsync(password);
                if (RememberSessionPassword)
                {
                    _sessionPassword = password;
                    OnPropertyChanged(nameof(HasSessionPassword));
                }

                _contentStream = stream;
                PrepareResumeFromRecent(path);

                _media = new Media(_libVlc, new StreamMediaInput(stream));
                MediaPlayer.Play(_media);

                IsEnded = false;
                PositionMs = 0;
                DurationMs = 0;
                State = PlayerViewState.Playing;
            }
        }
        catch (MsvInvalidPasswordException)
        {
            UnlockError = "密码不正确，请重试。";
        }
        catch (Exception ex)
        {
            UnlockError = ex.Message;
        }
        finally
        {
            IsUnlockBusy = false;
        }
    }

    [RelayCommand]
    private void CancelUnlock()
    {
        UnlockPassword = string.Empty;
        UnlockError = string.Empty;
        CloseMedia();
    }

    [RelayCommand]
    private void ClearSessionPassword()
    {
        _sessionPassword = null;
        OnPropertyChanged(nameof(HasSessionPassword));
    }

    // ---------- 播放控制 ----------

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (State != PlayerViewState.Playing)
        {
            return;
        }

        if (IsEnded)
        {
            Replay();
            return;
        }

        if (MediaPlayer.IsPlaying)
        {
            MediaPlayer.Pause();
        }
        else
        {
            MediaPlayer.Play();
        }
    }

    private void Replay()
    {
        if (_media is null)
        {
            return;
        }

        MediaPlayer.Stop();
        MediaPlayer.Play(_media);
        IsEnded = false;
        PositionMs = 0;
    }

    public void SeekToMs(long ms)
    {
        if (State != PlayerViewState.Playing)
        {
            return;
        }

        long duration = MediaPlayer.Length;
        if (duration <= 0)
        {
            return;
        }

        ms = Math.Clamp(ms, 0, duration);
        MediaPlayer.Time = ms;
        PositionMs = ms;
        IsEnded = false;
    }

    public void SeekRelative(long deltaMs) => SeekToMs(MediaPlayer.Time + deltaMs);

    [RelayCommand]
    private void SkipBackward() => SeekRelative(-5_000);

    [RelayCommand]
    private void SkipForward() => SeekRelative(5_000);

    [RelayCommand]
    private void ToggleMute() => MediaPlayer.Mute = !MediaPlayer.Mute;

    partial void OnVolumeChanged(double value)
    {
        MediaPlayer.Volume = (int)Math.Round(Math.Clamp(value, 0, 100));
    }

    [RelayCommand]
    private void ToggleTopmost() => IsTopmost = !IsTopmost;

    // ---------- 菜单操作 ----------

    private void CloseMenus()
    {
        IsSpeedMenuOpen = false;
        IsAudioMenuOpen = false;
        IsSubtitleMenuOpen = false;
    }

    [RelayCommand]
    private void ToggleSpeedMenu()
    {
        bool open = !IsSpeedMenuOpen;
        CloseMenus();
        IsSpeedMenuOpen = open;
    }

    [RelayCommand]
    private void ToggleAudioMenu()
    {
        bool open = !IsAudioMenuOpen;
        CloseMenus();
        IsAudioMenuOpen = open;
        if (open)
        {
            RefreshAudioTracks();
        }
    }

    [RelayCommand]
    private void ToggleSubtitleMenu()
    {
        bool open = !IsSubtitleMenuOpen;
        CloseMenus();
        IsSubtitleMenuOpen = open;
        if (open)
        {
            RefreshSubtitleTracks();
        }
    }

    [RelayCommand]
    private void SetRate(SpeedOption? option)
    {
        if (option is null)
        {
            return;
        }

        MediaPlayer.SetRate((float)option.Value);
        CurrentRateText = option.Label == "正常" ? "1×" : option.Label;
        foreach (SpeedOption item in SpeedOptions)
        {
            item.IsSelected = item == option;
        }

        IsSpeedMenuOpen = false;
    }

    private void RefreshAudioTracks()
    {
        AudioTracks.Clear();
        TrackDescription[]? descriptions = MediaPlayer.AudioTrackDescription;
        if (descriptions is null)
        {
            return;
        }

        int current = MediaPlayer.AudioTrack;
        foreach (TrackDescription track in descriptions)
        {
            AudioTracks.Add(new TrackItem(track.Id, string.IsNullOrEmpty(track.Name) ? $"音轨 {track.Id}" : track.Name, track.Id == current));
        }
    }

    [RelayCommand]
    private void SelectAudioTrack(TrackItem? item)
    {
        if (item is null)
        {
            return;
        }

        MediaPlayer.SetAudioTrack(item.Id);
        IsAudioMenuOpen = false;
    }

    private void RefreshSubtitleTracks()
    {
        SubtitleTracks.Clear();
        int current = MediaPlayer.Spu;
        SubtitleTracks.Add(new TrackItem(-1, "关闭字幕", current == -1));

        TrackDescription[]? descriptions = MediaPlayer.SpuDescription;
        if (descriptions is null)
        {
            return;
        }

        foreach (TrackDescription track in descriptions)
        {
            if (track.Id >= 0)
            {
                SubtitleTracks.Add(new TrackItem(track.Id, string.IsNullOrEmpty(track.Name) ? $"字幕 {track.Id}" : track.Name, track.Id == current));
            }
        }
    }

    [RelayCommand]
    private void SelectSubtitleTrack(TrackItem? item)
    {
        if (item is null)
        {
            return;
        }

        MediaPlayer.SetSpu(item.Id);
        IsSubtitleMenuOpen = false;
    }

    [RelayCommand]
    private void LoadSubtitleFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择字幕文件",
            Filter = "字幕文件|*.srt;*.ass;*.ssa;*.vtt;*.sub|所有文件|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            MediaPlayer.AddSlave(MediaSlaveType.Subtitle, new Uri(dialog.FileName).AbsoluteUri, true);
            RefreshSubtitleTracks();
        }
        catch
        {
            // 字幕加载失败不影响播放
        }
    }

    // ---------- 本次列表 ----------

    [RelayCommand]
    private async Task OpenPlaylistItemAsync(PlaylistItem? item)
    {
        if (item is not null)
        {
            await OpenPathAsync(item.Path);
        }
    }

    [RelayCommand]
    private async Task PlayPreviousAsync()
    {
        int index = CurrentPlaylistIndex;
        if (index > 0)
        {
            await OpenPathAsync(Playlist[index - 1].Path);
        }
    }

    [RelayCommand]
    private async Task PlayNextAsync()
    {
        int index = CurrentPlaylistIndex;
        if (index >= 0 && index < Playlist.Count - 1)
        {
            await OpenPathAsync(Playlist[index + 1].Path);
        }
    }

    [RelayCommand]
    private void RemovePlaylistItem(PlaylistItem? item)
    {
        if (item is null)
        {
            return;
        }

        bool wasActive = item.IsActive || string.Equals(item.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
        Playlist.Remove(item);
        if (wasActive)
        {
            CloseMedia();
        }

        UpdatePlaylistNavigation();
    }

    [RelayCommand]
    private void ClearPlaylist()
    {
        bool hadActive = Playlist.Any(item =>
            string.Equals(item.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (hadActive)
        {
            CloseMedia();
        }

        _thumbnailGeneration++;
        Playlist.Clear();
        UpdatePlaylistNavigation();
    }

    private void UpdatePlaylistNavigation()
    {
        foreach (PlaylistItem item in Playlist)
        {
            item.IsActive = !string.IsNullOrEmpty(CurrentPath)
                && string.Equals(item.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
        }

        NotifyPlaylistNavigation();
    }

    private void NotifyPlaylistNavigation()
    {
        OnPropertyChanged(nameof(HasPlaylist));
        OnPropertyChanged(nameof(PlaylistSummary));
        OnPropertyChanged(nameof(CurrentPlaylistIndex));
        OnPropertyChanged(nameof(HasPrevious));
        OnPropertyChanged(nameof(HasNext));
    }

    /// <summary>为照片列表后台生成缩略图；仅在本次运行记住密码时执行。</summary>
    private async Task LoadPlaylistThumbnailsAsync(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        int generation = _thumbnailGeneration;
        List<PlaylistItem> items = Playlist.Where(item => item.IsPhoto && item.Thumbnail is null).ToList();
        foreach (PlaylistItem item in items)
        {
            if (generation != _thumbnailGeneration || !Playlist.Contains(item) || item.Thumbnail is not null)
            {
                continue;
            }

            if (item.IsThumbnailLoading)
            {
                continue;
            }

            item.IsThumbnailLoading = true;
            await _thumbnailSemaphore.WaitAsync();
            try
            {
                string path = item.Path;
                System.Windows.Media.ImageSource image = await Task.Run(() =>
                {
                    using EncryptedVideoStream stream = MsvVideo.OpenPlaybackStream(path, password);
                    return PhotoImageLoader.Load(stream, 320).Image;
                });

                if (generation == _thumbnailGeneration)
                {
                    item.Thumbnail = image;
                }
            }
            catch (Exception ex)
            {
                item.ThumbnailError = ex.Message;
            }
            finally
            {
                item.IsThumbnailLoading = false;
                _thumbnailSemaphore.Release();
            }
        }
    }

    // ---------- 锁定 / 关闭 ----------

    [RelayCommand]
    private void Lock()
    {
        if (State != PlayerViewState.Playing && State != PlayerViewState.ViewingPhoto)
        {
            return;
        }

        SaveCurrentPosition(force: true);
        ReleaseMedia();
        UnlockError = string.Empty;
        UnlockPassword = HasSessionPassword ? _sessionPassword! : string.Empty;
        State = PlayerViewState.Unlock;
    }

    [RelayCommand]
    private void CloseMedia()
    {
        SaveCurrentPosition(force: true);
        ReleaseMedia();
        State = PlayerViewState.Home;
        CurrentPath = null;
        CurrentKind = MediaKind.Video;
        CurrentFileName = string.Empty;
        FileSizeText = string.Empty;
        WindowTitle = "密影播放器";
        UnlockPassword = string.Empty;
        UnlockError = string.Empty;
        IsHomeErrorVisible = false;
        _pendingResumeMs = null;
        IsResumeNoticeVisible = false;
    }

    [RelayCommand]
    private void RestartFromBeginning()
    {
        SeekToMs(0);
        IsResumeNoticeVisible = false;
    }

    private void ReleaseMedia()
    {
        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
            // 忽略停止异常
        }

        try
        {
            _media?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }

        _media = null;

        try
        {
            _contentStream?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }

        _contentStream = null;

        PhotoSource = null;
        PhotoInfoText = string.Empty;
        PhotoPixelWidth = 0;
        PhotoPixelHeight = 0;

        AudioTracks.Clear();
        SubtitleTracks.Clear();
        IsPlaying = false;
        IsEnded = false;
        IsSpeedMenuOpen = false;
        IsAudioMenuOpen = false;
        IsSubtitleMenuOpen = false;
        PositionMs = 0;
        DurationMs = 0;
        CurrentRateText = "1×";
        foreach (SpeedOption option in SpeedOptions)
        {
            option.IsSelected = option.Value == 1.0;
        }
    }

    /// <summary>窗口关闭时调用：保存进度并释放资源。</summary>
    public void Shutdown()
    {
        SaveCurrentPosition(force: true);
        Settings.Volume = (int)Math.Round(Volume);
        _store.Save();

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
            // 忽略
        }

        _media?.Dispose();
        _media = null;
        _contentStream?.Dispose();
        _contentStream = null;
        PhotoSource = null;
        PhotoInfoText = string.Empty;

        MediaPlayer.Dispose();
        _libVlc.Dispose();
    }

    // ---------- 最近播放 / 记录 ----------

    private void UpdateRecentKind(string path, MediaKind kind)
    {
        RecentItem? item = Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        item.Kind = kind == MediaKind.Photo ? "Photo" : "Video";
        SaveStore();
    }

    private void AddToRecent(string path, MediaKind kind)
    {
        if (!Settings.TrackRecent)
        {
            return;
        }

        RecentItem? existing = Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            Recent.Remove(existing);
        }
        else
        {
            existing = new RecentItem { Path = path };
        }

        existing.Kind = kind == MediaKind.Photo ? "Photo" : "Video";
        existing.LastPlayedUtc = DateTime.UtcNow;
        Recent.Insert(0, existing);

        while (Recent.Count > 12)
        {
            Recent.RemoveAt(Recent.Count - 1);
        }

        SaveStore();
    }

    [RelayCommand]
    private void RemoveRecent(RecentItem? item)
    {
        if (item is null)
        {
            return;
        }

        Recent.Remove(item);
        SaveStore();
    }

    [RelayCommand]
    private void ClearRecent()
    {
        Recent.Clear();
        SaveStore();
    }

    private void PrepareResumeFromRecent(string path)
    {
        _pendingResumeMs = null;
        if (CurrentKind == MediaKind.Photo || !Settings.RememberPosition)
        {
            return;
        }

        RecentItem? item = Recent.FirstOrDefault(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (item is not null && item.PositionMs > 30_000)
        {
            _pendingResumeMs = item.PositionMs;
        }
    }

    private void ApplyPendingResume()
    {
        if (_pendingResumeMs is not long position || DurationMs <= 0)
        {
            return;
        }

        _pendingResumeMs = null;

        if (position > 30_000 && position < DurationMs - 20_000)
        {
            MediaPlayer.Time = position;
            ResumeNoticeText = $"已从 {FormatTime(position)} 继续播放";
            IsResumeNoticeVisible = true;
            _ = HideResumeNoticeLaterAsync();
        }
    }

    private async Task HideResumeNoticeLaterAsync()
    {
        await Task.Delay(9000);
        await _dispatcher.InvokeAsync(() => IsResumeNoticeVisible = false);
    }

    private void SaveCurrentPosition(bool force = false)
    {
        if (CurrentPath is null || CurrentKind == MediaKind.Photo)
        {
            return;
        }

        RecentItem? item = Recent.FirstOrDefault(r => string.Equals(r.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return;
        }

        try
        {
            long position = MediaPlayer.Time;
            long duration = MediaPlayer.Length;

            if (!Settings.RememberPosition)
            {
                item.PositionMs = 0;
            }
            else if (duration > 0 && position > 0 && position < duration - 5_000)
            {
                item.PositionMs = position;
                item.DurationMs = duration;
            }
            else if (duration > 0 && position >= duration - 5_000)
            {
                item.PositionMs = 0;
                item.DurationMs = duration;
            }

            item.LastPlayedUtc = DateTime.UtcNow;
        }
        catch
        {
            // 忽略读取进度失败
        }

        if (force)
        {
            SaveStore();
        }
    }

    private void MaybeSavePosition(long time)
    {
        if (CurrentKind == MediaKind.Photo || !Settings.RememberPosition || CurrentPath is null)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastPositionSave).TotalSeconds < 10)
        {
            return;
        }

        _lastPositionSave = DateTime.UtcNow;
        RecentItem? item = Recent.FirstOrDefault(r => string.Equals(r.Path, CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (item is not null && MediaPlayer.Length > time + 5_000)
        {
            item.PositionMs = time;
            item.DurationMs = MediaPlayer.Length;
            item.LastPlayedUtc = DateTime.UtcNow;
            SaveStore();
        }
    }

    private void SaveStore()
    {
        _store.State.Recent = Recent.ToList();
        _store.Save();
    }

    // ---------- 设置 ----------

    public bool RememberPosition
    {
        get => Settings.RememberPosition;
        set
        {
            if (Settings.RememberPosition == value)
            {
                return;
            }

            Settings.RememberPosition = value;
            if (!value)
            {
                foreach (RecentItem item in Recent)
                {
                    item.PositionMs = 0;
                }
            }

            OnPropertyChanged();
            SaveStore();
        }
    }

    public bool TrackRecent
    {
        get => Settings.TrackRecent;
        set
        {
            if (Settings.TrackRecent == value)
            {
                return;
            }

            Settings.TrackRecent = value;
            OnPropertyChanged();
            SaveStore();
        }
    }


    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        IsHomeErrorVisible = false;
    }

    // ---------- VLC 事件 ----------

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        long time = e.Time;
        _dispatcher.InvokeAsync(() =>
        {
            if (State != PlayerViewState.Playing || IsSeeking)
            {
                return;
            }

            PositionMs = time;
            MaybeSavePosition(time);
        });
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        long length = e.Length;
        _dispatcher.InvokeAsync(() =>
        {
            DurationMs = length;
            OnPropertyChanged(nameof(IsValidDuration));
            ApplyPendingResume();
        });
    }

    private void OnPlaying(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            IsPlaying = true;
            IsEnded = false;
        });

    private void OnPaused(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() => IsPlaying = false);

    private void OnStopped(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() => IsPlaying = false);

    private void OnEndReached(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            IsPlaying = false;
            IsEnded = true;
            SaveCurrentPosition(force: true);
        });

    private void OnEncounteredError(object? sender, EventArgs e)
        => _dispatcher.InvokeAsync(() =>
        {
            IsPlaying = false;
            UnlockError = "无法解码该视频（文件可能已损坏），请尝试其他文件。";
            if (State == PlayerViewState.Playing)
            {
                ReleaseMedia();
                State = PlayerViewState.Unlock;
            }
        });

    private static string BuildPhotoInfo(PhotoLoadResult photo)
    {
        var parts = new List<string> { $"{photo.PixelWidth} × {photo.PixelHeight}" };
        if (photo.TakenAt is DateTime taken)
        {
            parts.Add(taken.ToString("yyyy-MM-dd HH:mm"));
        }

        string? camera = string.Join(
            " ",
            new[] { photo.CameraMake, photo.CameraModel }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct());
        if (!string.IsNullOrWhiteSpace(camera))
        {
            parts.Add(camera);
        }

        return string.Join(" · ", parts);
    }

    // ---------- 工具 ----------

    public static string FormatTime(long ms)
    {
        if (ms < 0)
        {
            ms = 0;
        }

        TimeSpan t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}