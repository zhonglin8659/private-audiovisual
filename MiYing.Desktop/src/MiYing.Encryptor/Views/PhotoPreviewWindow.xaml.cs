using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MiYing.UI;
using Wpf.Ui.Controls;

namespace MiYing.Encryptor.Views;

public partial class PhotoPreviewWindow : FluentWindow
{
    private readonly string _path;

    public PhotoPreviewWindow(string path)
    {
        _path = path;
        InitializeComponent();

        string name = Path.GetFileName(path);
        Title = name + " — 照片预览";
        TitleBarControl.Title = Title;

        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Cut,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));

        Loaded += OnWindowLoadedAsync;
    }

    private async void OnWindowLoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            PhotoLoadResult result = await Task.Run(() => PhotoImageLoader.Load(_path, 6000));
            Viewer.Source = result.Image;
            InfoText.Text = BuildInfo(result);
            LoadingRing.Visibility = Visibility.Collapsed;
            Viewer.Focus();
        }
        catch (Exception ex)
        {
            LoadingRing.Visibility = Visibility.Collapsed;
            ErrorText.Text = ex.Message;
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private static string BuildInfo(PhotoLoadResult result)
    {
        var parts = new List<string> { $"{result.PixelWidth} × {result.PixelHeight}" };
        if (result.TakenAt is DateTime taken)
        {
            parts.Add(taken.ToString("yyyy-MM-dd HH:mm"));
        }

        string? camera = string.Join(
            " ",
            new[] { result.CameraMake, result.CameraModel }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct());
        if (!string.IsNullOrWhiteSpace(camera))
        {
            parts.Add(camera);
        }

        return string.Join(" · ", parts);
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Viewer.ZoomOut();

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Viewer.ZoomIn();

    private void OnFitClick(object sender, RoutedEventArgs e) => Viewer.FitToWindow();

    private void OnActualSizeClick(object sender, RoutedEventArgs e) => Viewer.ActualSize();

    private void OnRotateClick(object sender, RoutedEventArgs e) => Viewer.RotateClockwise();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCopyShortcut(e))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Add:
            case Key.OemPlus:
                Viewer.ZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                Viewer.ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                Viewer.FitToWindow();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                Viewer.ActualSize();
                e.Handled = true;
                break;
            case Key.R:
                Viewer.RotateClockwise();
                e.Handled = true;
                break;
        }
    }

    private static bool IsCopyShortcut(KeyEventArgs e)
    {
        ModifierKeys modifiers = Keyboard.Modifiers;
        return modifiers.HasFlag(ModifierKeys.Control) && (e.Key == Key.C || e.Key == Key.Insert);
    }
}