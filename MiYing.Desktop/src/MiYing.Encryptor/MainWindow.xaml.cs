using System.ComponentModel;
using System.IO;
using System.Windows;
using MiYing.Encryptor.Services;
using MiYing.Encryptor.ViewModels;
using MiYing.Encryptor.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MiYing.Encryptor;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel = new();
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        ApplyWindows10Compatibility();
        UpdateThemeVisual();
        Loaded += OnWindowLoadedAsync;
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

    private async void OnWindowLoadedAsync(object sender, RoutedEventArgs e)
    {
        // 确保窗口以正常状态显示（防止被启动器或宿主以“最小化/隐藏”方式拉起）
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        if (App.ScreenshotPath is not string screenshotPrefix)
        {
            return;
        }

        if (App.PhotoDemo)
        {
            _viewModel.Mode = WorkMode.PhotoEncrypt;
        }

        if (App.DemoFiles.Count > 0)
        {
            _viewModel.AddPaths(App.DemoFiles);
        }

        if (App.PreviewDemoFile is string previewFile && File.Exists(previewFile))
        {
            var previewWindow = new PhotoPreviewWindow(previewFile) { Owner = this };
            previewWindow.Show();
        }

        await Task.Delay(1200);
        QaCapture.CaptureWindows(screenshotPrefix);
        Application.Current.Shutdown();
    }

    private void OnThemeButtonClick(object sender, RoutedEventArgs e)
    {
        AppThemeMode next = App.CurrentThemeMode switch
        {
            AppThemeMode.Light => AppThemeMode.Dark,
            AppThemeMode.Dark => AppThemeMode.System,
            _ => AppThemeMode.Light,
        };

        App.ApplyTheme(next);
        UpdateThemeVisual();
    }

    private void UpdateThemeVisual()
    {
        ThemeIcon.Symbol = App.CurrentThemeMode switch
        {
            AppThemeMode.Light => SymbolRegular.WeatherSunny24,
            AppThemeMode.Dark => SymbolRegular.WeatherMoon24,
            _ => SymbolRegular.Desktop24,
        };

        string name = App.CurrentThemeMode switch
        {
            AppThemeMode.Light => "浅色",
            AppThemeMode.Dark => "深色",
            _ => "跟随系统",
        };

        ThemeButton.ToolTip = $"当前主题：{name}（点击切换）";

        if (App.CurrentThemeMode == AppThemeMode.System)
        {
            SystemThemeWatcher.Watch(this);
        }
        else
        {
            SystemThemeWatcher.UnWatch(this);
        }
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.AddPaths(paths);
        }

        e.Handled = true;
    }

    private void OnPhotoImageMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: FileItemViewModel item })
        {
            var window = new PhotoPreviewWindow(item.SourcePath) { Owner = this };
            window.ShowDialog();
            e.Handled = true;
        }
    }

    private async void OnDeleteOriginalChecked(object sender, RoutedEventArgs e)
    {
        Wpf.Ui.Controls.MessageBoxResult result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "确认删除原文件",
            Content = "处理完成后，原文件将被移入回收站（可随时还原）。\n\n确定要开启此选项吗？",
            PrimaryButtonText = "开启",
            CloseButtonText = "取消",
        }.ShowDialogAsync();

        if (result != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            _viewModel.DeleteOriginalAfter = false;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || !_viewModel.IsBusy)
        {
            return;
        }

        e.Cancel = true;

        Wpf.Ui.Controls.MessageBoxResult result = await new Wpf.Ui.Controls.MessageBox
        {
            Title = "任务进行中",
            Content = "当前正在处理文件，退出将取消任务（未完成的文件不会留下半成品）。\n\n确定要退出吗？",
            PrimaryButtonText = "退出",
            CloseButtonText = "继续处理",
        }.ShowDialogAsync();

        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            _viewModel.CancelCurrent();
            await _viewModel.WaitForIdleAsync();
            _allowClose = true;
            Close();
        }
    }
}