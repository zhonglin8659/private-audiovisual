using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.WPF;
using MiYing.Player.Models;
using MiYing.Player.Services;
using MiYing.Player.ViewModels;
using Wpf.Ui.Controls;

namespace MiYing.Player;

public partial class MainWindow : FluentWindow
{
    private readonly PlayerViewModel _viewModel = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _clickTimer;
    private static readonly PropertyInfo? VideoForegroundWindowProperty =
        typeof(VideoView).GetProperty("ForegroundWindow", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? AlignVideoForegroundWindowMethod =
        typeof(VideoView).Assembly.GetType("LibVLCSharp.WPF.ForegroundWindow")
            ?.GetMethod("AlignWithBackground", BindingFlags.Instance | BindingFlags.NonPublic);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetDoubleClickTime();

    private bool _isSeeking;
    private bool _isFullscreen;
    private bool _controlsVisible;
    private bool _shutdownDone;
    private WindowState _previousState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        ApplyWindows10Compatibility();

        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _idleTimer.Tick += OnIdleTick;

        _clickTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(200, GetDoubleClickTime())),
        };
        _clickTimer.Tick += OnSingleClickTick;

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Cut,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        EnsureWindowVisible();

        // 截图模式（QA）
        if (App.ScreenshotPath is string screenshotPrefix)
        {
            await Task.Delay(800);
            QaCapture.CaptureWindows(screenshotPrefix);
            Application.Current.Shutdown();
            return;
        }

        // 播放自检模式（QA）
        if (App.SelfTestPath is string selfTestPath)
        {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MIYING_SELFTEST_PLAYLIST")))
            {
                await RunPlaylistSelfTestAsync(selfTestPath);
            }
            else
            {
                await RunSelfTestAsync(selfTestPath);
            }
            return;
        }

        if (App.StartupFilePath is string path && File.Exists(path))
        {
            await _viewModel.OpenPathAsync(path);
        }
    }

    /// <summary>自动化解锁 + 播放自检，结果写入 %TEMP%\miying-selftest.log。</summary>
    private async Task RunSelfTestAsync(string path)
    {
        var lines = new List<string>();
        int exitCode = 1;
        try
        {
            string password = Environment.GetEnvironmentVariable("MIYING_SELFTEST_PASSWORD") ?? string.Empty;
            lines.Add("opening=" + path);
            await _viewModel.OpenPathAsync(path);
            lines.Add("state_after_open=" + _viewModel.State + " error=" + _viewModel.UnlockError);

            await _viewModel.UnlockWithPasswordAsync(password);
            lines.Add("state_after_unlock=" + _viewModel.State + " error=" + _viewModel.UnlockError);

            if (_viewModel.State == PlayerViewState.ViewingPhoto)
            {
                lines.Add($"photo={_viewModel.PhotoPixelWidth}x{_viewModel.PhotoPixelHeight} source={_viewModel.PhotoSource is not null}");
                await Task.Delay(600);
                QaCapture.CaptureWindows(Path.Combine(Path.GetTempPath(), "miying-selftest"));

                PhotoViewer.ZoomIn();
                PhotoViewer.ZoomIn();
                await Task.Delay(300);
                lines.Add($"photo_zoom={PhotoViewer.ZoomPercent:0.#} fit={PhotoViewer.IsFitMode}");
                QaCapture.CaptureWindows(Path.Combine(Path.GetTempPath(), "miying-selftest-zoom"));
                PhotoViewer.ActualSize();
                lines.Add($"photo_actual={PhotoViewer.ZoomPercent:0.#}");
                PhotoViewer.FitToWindow();

                if (_viewModel.PhotoSource is not null && _viewModel.PhotoPixelWidth > 0)
                {
                    exitCode = 0;
                }
            }
            else
            {
                bool advanced = false;
                long last = -1;
                for (int i = 0; i < 80 && _viewModel.State == PlayerViewState.Playing; i++)
                {
                    await Task.Delay(250);
                    long position = (long)_viewModel.PositionMs;
                    if (position > last + 100)
                    {
                        advanced = true;
                    }

                    last = position;
                    if (advanced && _viewModel.DurationMs > 0)
                    {
                        break;
                    }
                }

                lines.Add($"duration={_viewModel.DurationMs} position={_viewModel.PositionMs} advanced={advanced} isPlaying={_viewModel.IsPlaying}");

                ShowControls();
                await Task.Delay(600);
                QaCapture.CaptureWindows(Path.Combine(Path.GetTempPath(), "miying-selftest"));

                if (_viewModel.State == PlayerViewState.Playing && _viewModel.DurationMs > 0 && advanced)
                {
                    _viewModel.SeekToMs(1500);
                    await Task.Delay(500);
                    lines.Add("after_seek=" + _viewModel.PositionMs);

                    // 跳到 31.5 秒并停留，验证「继续播放」记录
                    _viewModel.SeekToMs(31_500);
                    await Task.Delay(1200);
                    lines.Add("after_seek_saved=" + _viewModel.PositionMs);

                    if (Environment.GetEnvironmentVariable("MIYING_SELFTEST_OVERLAY") == "1")
                    {
                        _viewModel.CloseMediaCommand.Execute(null);
                        await Task.Delay(500);
                        Window? closedOverlay = VideoForegroundWindowProperty?.GetValue(PlayerView) as Window;
                        lines.Add("overlay_state_after_close=" + _viewModel.State);
                        lines.Add("overlay_player_visible_after_close=" + PlayerView.Visibility);
                        lines.Add("overlay_foreground_visible_after_close=" + closedOverlay?.IsVisible.ToString());

                        await _viewModel.OpenPathAsync(path);
                        await _viewModel.UnlockWithPasswordAsync(password);
                        await Task.Delay(900);
                        Window? reopenedOverlay = VideoForegroundWindowProperty?.GetValue(PlayerView) as Window;
                        lines.Add("overlay_state_after_reopen=" + _viewModel.State);
                        lines.Add("overlay_foreground_visible_after_reopen=" + reopenedOverlay?.IsVisible.ToString());
                    }

                    exitCode = 0;
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add("EXCEPTION " + ex);
        }

        try
        {
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "miying-selftest.log"), lines);
        }
        catch
        {
            // 忽略
        }

        Application.Current.Shutdown(exitCode);
    }

    /// <summary>多选列表 / 照片缩略图 / 上一张下一张自检（QA）。</summary>
    private async Task RunPlaylistSelfTestAsync(string firstPath)
    {
        var lines = new List<string>();
        int exitCode = 1;
        try
        {
            string password = Environment.GetEnvironmentVariable("MIYING_SELFTEST_PASSWORD") ?? string.Empty;
            string[] extra = (Environment.GetEnvironmentVariable("MIYING_SELFTEST_PLAYLIST") ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string[] paths = new[] { firstPath }.Concat(extra).ToArray();

            _viewModel.RememberSessionPassword = true;
            await _viewModel.OpenFilesAsync(paths, replacePlaylist: true);
            lines.Add("playlist_count=" + _viewModel.Playlist.Count);
            lines.Add("playlist_state_home=" + (_viewModel.State == PlayerViewState.Home));
            lines.Add("playlist_has_next_before_open=" + _viewModel.HasNext);

            int holdMs = int.TryParse(Environment.GetEnvironmentVariable("MIYING_SELFTEST_HOLD_MS"), out int parsedHold) ? parsedHold : 500;
            await Task.Delay(holdMs);
            QaCapture.CaptureWindows(Path.Combine(Path.GetTempPath(), "miying-playlist-home"));

            await _viewModel.OpenPathAsync(paths[0]);
            await _viewModel.UnlockWithPasswordAsync(password);
            lines.Add("playlist_first_state=" + _viewModel.State);
            lines.Add("playlist_first_index=" + _viewModel.CurrentPlaylistIndex);
            lines.Add("playlist_first_has_next=" + _viewModel.HasNext);

            await Task.Delay(2600);
            lines.Add("playlist_thumbnail_count=" + _viewModel.Playlist.Count(item => item.Thumbnail is not null));


            if (_viewModel.State == PlayerViewState.Playing && _viewModel.HasNext)
            {
                _viewModel.PlayNextCommand.Execute(null);
                await Task.Delay(1800);
                lines.Add("playlist_after_video_next_state=" + _viewModel.State);
                lines.Add("playlist_after_video_next_index=" + _viewModel.CurrentPlaylistIndex);
                lines.Add("playlist_after_video_next_has_previous=" + _viewModel.HasPrevious);
            }
            if (_viewModel.State == PlayerViewState.ViewingPhoto && _viewModel.HasNext)
            {
                _viewModel.PlayNextCommand.Execute(null);
                await Task.Delay(1600);
                lines.Add("playlist_after_next_state=" + _viewModel.State);
                lines.Add("playlist_after_next_index=" + _viewModel.CurrentPlaylistIndex);
                lines.Add("playlist_after_next_has_previous=" + _viewModel.HasPrevious);
            }

            _viewModel.CloseMediaCommand.Execute(null);
            await Task.Delay(500);
            Window? foreground = VideoForegroundWindowProperty?.GetValue(PlayerView) as Window;
            lines.Add("playlist_after_close_state=" + _viewModel.State);
            lines.Add("playlist_after_close_foreground_visible=" + foreground?.IsVisible.ToString());
            exitCode = 0;
        }
        catch (Exception ex)
        {
            lines.Add("EXCEPTION " + ex);
        }

        try
        {
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "miying-playlist-selftest.log"), lines);
        }
        catch
        {
            // 忽略
        }

        Application.Current.Shutdown(exitCode);
    }
    /// <summary>确保窗口以正常状态显示（防止被启动器或宿主以“最小化/隐藏”方式拉起）。</summary>
    private void EnsureWindowVisible()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>Windows 10 兼容处理：不使用 Mica/Acrylic 背景材质，改用不透明背景，避免个别显卡驱动下窗口不可见。</summary>
    private void ApplyWindows10Compatibility()
    {
        if (Environment.OSVersion.Version.Build < 22000)
        {
            WindowBackdropType = WindowBackdropType.None;
            if (TryFindResource("SolidBackgroundFillColorBaseBrush") is System.Windows.Media.Brush brush)
            {
                Background = brush;
            }
        }
    }

    // ---------- 状态联动 ----------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.State))
        {
            PlayerView.Visibility = _viewModel.State == PlayerViewState.Playing
                ? Visibility.Visible
                : Visibility.Collapsed;

            // LibVLCSharp.WPF 的画面覆盖层是独立的顶层窗口；仅隐藏 VideoView
            // 不会自动隐藏该窗口，会在首页上方留下透明层并吞掉所有点击。
            if (_viewModel.State != PlayerViewState.Playing)
            {
                HideVideoForegroundWindow();
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (_viewModel.State == PlayerViewState.Unlock)
                {
                    UnlockPasswordBox.Focus();
                    Keyboard.Focus(UnlockPasswordBox);
                }
                else if (_viewModel.State == PlayerViewState.Playing)
                {
                    ShowVideoForegroundWindow();
                    ShowControls();
                }
                else if (_viewModel.State == PlayerViewState.ViewingPhoto)
                {
                    ShowControls();
                    _idleTimer.Stop();
                    _idleTimer.Start();
                    PhotoViewer.Focus();
                }
            });
        }
    }


    // ---------- 控制层显示 / 隐藏 ----------

    private void OnOverlayMouseMove(object sender, MouseEventArgs e)
    {
        ShowControls();
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel.State != PlayerViewState.Playing && _viewModel.State != PlayerViewState.ViewingPhoto)
        {
            return;
        }

        ShowControls();
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    private void OnIdleTick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();

        if (_viewModel.State == PlayerViewState.Playing)
        {
            if (!_viewModel.IsPlaying)
            {
                return;
            }
        }
        else if (_viewModel.State != PlayerViewState.ViewingPhoto)
        {
            return;
        }

        if (_isSeeking
            || _viewModel.IsSpeedMenuOpen
            || _viewModel.IsAudioMenuOpen
            || _viewModel.IsSubtitleMenuOpen)
        {
            return;
        }

        HideControls();
    }

    private void HideVideoForegroundWindow()
    {
        if (VideoForegroundWindowProperty?.GetValue(PlayerView) is Window foreground)
        {
            try
            {
                foreground.Hide();
            }
            catch
            {
                // 播放器切换状态时窗口可能已被释放，忽略即可。
            }
        }
    }

    private void ShowVideoForegroundWindow()
    {
        if (VideoForegroundWindowProperty?.GetValue(PlayerView) is Window foreground)
        {
            try
            {
                if (!foreground.IsVisible)
                {
                    foreground.Show();
                }

                AlignVideoForegroundWindowMethod?.Invoke(foreground, null);
            }
            catch
            {
                // 覆盖层窗口由 LibVLCSharp 管理，异常时不影响播放。
            }
        }
    }

    private void ShowControls()
    {
        _controlsVisible = true;
        OverlayTop.Opacity = 1;
        OverlayBottom.Opacity = 1;
        OverlayTop.IsHitTestVisible = true;
        OverlayBottom.IsHitTestVisible = true;
        OverlayRoot.Cursor = Cursors.Arrow;

        PhotoOverlayTop.Opacity = 1;
        PhotoOverlayBottom.Opacity = 1;
        PhotoOverlayTop.IsHitTestVisible = true;
        PhotoOverlayBottom.IsHitTestVisible = true;
        PhotoViewer.Cursor = Cursors.Arrow;
    }

    private void HideControls()
    {
        _controlsVisible = false;
        OverlayTop.Opacity = 0;
        OverlayBottom.Opacity = 0;
        OverlayTop.IsHitTestVisible = false;
        OverlayBottom.IsHitTestVisible = false;
        OverlayRoot.Cursor = Cursors.None;

        PhotoOverlayTop.Opacity = 0;
        PhotoOverlayBottom.Opacity = 0;
        PhotoOverlayTop.IsHitTestVisible = false;
        PhotoOverlayBottom.IsHitTestVisible = false;
        PhotoViewer.Cursor = Cursors.None;
    }

    // ---------- 画面单击 / 双击 ----------

    private void OnOverlayMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            ToggleFullscreen();
            return;
        }

        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void OnSingleClickTick(object? sender, EventArgs e)
    {
        _clickTimer.Stop();

        // 控制栏隐藏时，单击先把它唤回来，不直接切换播放状态。
        if (!_controlsVisible)
        {
            ShowControls();
            _idleTimer.Stop();
            _idleTimer.Start();
            return;
        }

        _viewModel.TogglePlayPauseCommand.Execute(null);
    }

    // ---------- 进度条 ----------

    private void OnSeekSliderPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
        _viewModel.IsSeeking = true;
    }

    private void OnSeekSliderPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = false;
        _viewModel.IsSeeking = false;
        _viewModel.SeekToMs((long)PositionSlider.Value);
    }

    // ---------- 全屏 ----------

    private void OnFullscreenButtonClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (_viewModel.State != PlayerViewState.Playing && _viewModel.State != PlayerViewState.ViewingPhoto)
        {
            return;
        }

        if (!_isFullscreen)
        {
            _previousState = WindowState;
            TitleBarHost.Visibility = Visibility.Collapsed;
            WindowState = WindowState.Maximized;
            _isFullscreen = true;
        }
        else
        {
            WindowState = _previousState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            TitleBarHost.Visibility = Visibility.Visible;
            _isFullscreen = false;
        }

        ShowControls();
    }

    // ---------- 键盘 ----------

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCopyShortcut(e))
        {
            e.Handled = true;
            return;
        }

        if (UnlockPasswordBox.IsKeyboardFocusWithin && e.Key != Key.Enter)
        {
            return;
        }

        if (_viewModel.State == PlayerViewState.ViewingPhoto)
        {
            HandlePhotoKey(e);
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
            case Key.K:
                _viewModel.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_viewModel.State == PlayerViewState.Unlock)
                {
                    _viewModel.UnlockCommand.Execute(null);
                    e.Handled = true;
                }

                break;

            case Key.Left:
                _viewModel.SeekRelative(e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) ? -30_000 : -5_000);
                e.Handled = true;
                break;

            case Key.Right:
                _viewModel.SeekRelative(e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) ? 30_000 : 5_000);
                e.Handled = true;
                break;

            case Key.Up:
                _viewModel.Volume = Math.Min(100, _viewModel.Volume + 5);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.Volume = Math.Max(0, _viewModel.Volume - 5);
                e.Handled = true;
                break;

            case Key.M:
                _viewModel.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;

            case Key.Escape:
            case Key.Back:
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else if (_viewModel.State == PlayerViewState.Playing || _viewModel.State == PlayerViewState.ViewingPhoto)
                {
                    _viewModel.CloseMediaCommand.Execute(null);
                    e.Handled = true;
                }

                break;

            case Key.PageUp:
                _viewModel.PlayPreviousCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.PageDown:
                _viewModel.PlayNextCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.O:
                _viewModel.OpenFileCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void HandlePhotoKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                _viewModel.PlayPreviousCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.PlayNextCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                PhotoViewer.ZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                PhotoViewer.ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                PhotoViewer.FitToWindow();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                PhotoViewer.ActualSize();
                e.Handled = true;
                break;
            case Key.R:
                PhotoViewer.RotateClockwise();
                e.Handled = true;
                break;
            case Key.F:
            case Key.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case Key.Escape:
            case Key.Back:
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                    e.Handled = true;
                }
                else if (_viewModel.State == PlayerViewState.Playing || _viewModel.State == PlayerViewState.ViewingPhoto)
                {
                    _viewModel.CloseMediaCommand.Execute(null);
                    e.Handled = true;
                }

                break;
            case Key.O:
                _viewModel.OpenFileCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private static bool IsCopyShortcut(KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        return modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.C || e.Key == Key.Insert);
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        CapsLockHint.Visibility = Keyboard.IsKeyToggled(Key.CapsLock)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (e.Key == Key.Enter && !_viewModel.IsUnlockBusy)
        {
            _viewModel.UnlockCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ---------- 最近播放 ----------

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistItem item)
        {
            _viewModel.OpenPlaylistItemCommand.Execute(item);
        }
    }

    private async void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is RecentItem item)
        {
            await _viewModel.OpenPathAsync(item.Path);
        }
    }

    // ---------- 拖拽 ----------

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            string[] files = paths.Where(p =>
                p.EndsWith(".msv", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".msp", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length > 0)
            {
                await _viewModel.OpenFilesAsync(files, replacePlaylist: true);
            }
        }

        e.Handled = true;
    }

    // ---------- 照片操作 ----------

    private void OnPhotoZoomOutClick(object sender, RoutedEventArgs e) => PhotoViewer.ZoomOut();

    private void OnPhotoZoomInClick(object sender, RoutedEventArgs e) => PhotoViewer.ZoomIn();

    private void OnPhotoFitClick(object sender, RoutedEventArgs e) => PhotoViewer.FitToWindow();

    private void OnPhotoActualSizeClick(object sender, RoutedEventArgs e) => PhotoViewer.ActualSize();

    private void OnPhotoRotateClick(object sender, RoutedEventArgs e) => PhotoViewer.RotateClockwise();

    // ---------- 关闭 ----------

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone)
        {
            return;
        }

        _shutdownDone = true;
        HideVideoForegroundWindow();
        _idleTimer.Stop();
        _clickTimer.Stop();
        _viewModel.Shutdown();
    }
}