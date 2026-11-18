using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MiYing.UI;

/// <summary>
/// 单张照片预览控件：缩放、拖动、适应窗口 / 100%、旋转。
/// 默认拦截复制与剪切命令，照片内容不会通过 Ctrl+C 或右键菜单进入剪贴板。
/// </summary>
public partial class PhotoPreviewControl : UserControl
{
    private const double MinScale = 0.02;
    private const double MaxScale = 8.0;
    private const double ZoomStep = 1.2;
    private const double FitMargin = 32;

    private double _scale = 1.0;
    private double _pixelScale = 1.0;
    private double _rotation;
    private double _panX;
    private double _panY;
    private bool _fitMode = true;
    private bool _dragging;
    private Point _lastDragPoint;

    public PhotoPreviewControl()
    {
        InitializeComponent();
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Copy,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));
        CommandBindings.Add(new CommandBinding(
            ApplicationCommands.Cut,
            (_, e) => e.Handled = true,
            (_, e) => e.CanExecute = false));
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(PhotoPreviewControl),
        new PropertyMetadata(null, OnSourceChanged));

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double ZoomPercent => _pixelScale > 0 ? _scale / _pixelScale * 100 : _scale * 100;

    public bool IsFitMode => _fitMode;

    public void FitToWindow()
    {
        if (ImageHost.Source is null || Root.ActualWidth <= 0 || Root.ActualHeight <= 0)
        {
            return;
        }

        double imageWidth = ImageHost.ActualWidth;
        double imageHeight = ImageHost.ActualHeight;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        bool swapped = Math.Abs((int)_rotation % 180) == 90;
        double displayWidth = swapped ? imageHeight : imageWidth;
        double displayHeight = swapped ? imageWidth : imageHeight;
        double fit = Math.Min(
            (Root.ActualWidth - FitMargin) / displayWidth,
            (Root.ActualHeight - FitMargin) / displayHeight);
        fit = Math.Clamp(fit, MinScale, MaxScale);
        _fitMode = true;
        _scale = fit;
        _panX = 0;
        _panY = 0;
        UpdateCenters();
        ImageScale.ScaleX = fit;
        ImageScale.ScaleY = fit;
        UpdateTranslation();
    }

    public void ActualSize()
    {
        if (ImageHost.Source is null)
        {
            return;
        }

        _fitMode = false;
        _scale = _pixelScale;
        _panX = 0;
        _panY = 0;
        UpdateCenters();
        ImageScale.ScaleX = _scale;
        ImageScale.ScaleY = _scale;
        UpdateTranslation();
    }

    public void ZoomIn() => ZoomAt(new Point(Root.ActualWidth / 2, Root.ActualHeight / 2), ZoomStep);

    public void ZoomOut() => ZoomAt(new Point(Root.ActualWidth / 2, Root.ActualHeight / 2), 1 / ZoomStep);

    public void RotateClockwise()
    {
        if (ImageHost.Source is null)
        {
            return;
        }

        _rotation = (_rotation + 90) % 360;
        ImageRotation.Angle = _rotation;
        UpdateCenters();

        if (_fitMode)
        {
            FitToWindow();
        }
        else
        {
            UpdateTranslation();
        }
    }

    public void ResetView()
    {
        _rotation = 0;
        _scale = 1;
        _panX = 0;
        _panY = 0;
        _fitMode = true;
        ImageRotation.Angle = 0;
        UpdateCenters();
        UpdateTranslation();
        Dispatcher.BeginInvoke(new Action(FitToWindow), DispatcherPriority.Loaded);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (PhotoPreviewControl)d;
        control.ImageHost.Source = (ImageSource?)e.NewValue;
        control._pixelScale = e.NewValue is BitmapSource source && source.DpiX > 0 && source.DpiY > 0
            ? source.DpiX / 96.0
            : 1.0;
        control.ResetView();
    }

    private void ZoomAt(Point anchor, double factor)
    {
        if (ImageHost.Source is null)
        {
            return;
        }

        Point? imagePoint = null;
        Transform? transform = ImageHost.RenderTransform;
        try
        {
            if (transform is not null)
            {
                imagePoint = transform.Inverse.Transform(anchor);
            }
        }
        catch
        {
            // 逆变换失败时退化为居中缩放
        }

        double newScale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        if (Math.Abs(newScale - _scale) < 0.000001)
        {
            return;
        }

        _fitMode = false;
        _scale = newScale;
        ImageScale.ScaleX = _scale;
        ImageScale.ScaleY = _scale;
        UpdateCenters();

        if (imagePoint is Point point && transform is not null)
        {
            Point mapped = transform.Transform(point);
            _panX += anchor.X - mapped.X;
            _panY += anchor.Y - mapped.Y;
        }

        UpdateTranslation();
    }

    private void UpdateCenters()
    {
        double width = ImageHost.ActualWidth;
        double height = ImageHost.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        ImageScale.CenterX = width / 2;
        ImageScale.CenterY = height / 2;
        ImageRotation.CenterX = width / 2;
        ImageRotation.CenterY = height / 2;
    }

    private void UpdateTranslation()
    {
        if (ImageHost.ActualWidth <= 0 || ImageHost.ActualHeight <= 0)
        {
            return;
        }

        ImageTranslate.X = (Root.ActualWidth - ImageHost.ActualWidth) / 2 + _panX;
        ImageTranslate.Y = (Root.ActualHeight - ImageHost.ActualHeight) / 2 + _panY;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCenters();
        if (_fitMode)
        {
            FitToWindow();
        }
        else
        {
            UpdateTranslation();
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(new Action(FitToWindow), DispatcherPriority.Loaded);
        }
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ImageHost.Source is null)
        {
            return;
        }

        ZoomAt(e.GetPosition(Root), e.Delta > 0 ? ZoomStep : 1 / ZoomStep);
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ImageHost.Source is null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (_fitMode)
            {
                ActualSize();
            }
            else
            {
                FitToWindow();
            }

            e.Handled = true;
            return;
        }

        _dragging = true;
        _lastDragPoint = e.GetPosition(Root);
        Root.CaptureMouse();
        Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        Point current = e.GetPosition(Root);
        Vector delta = current - _lastDragPoint;
        _panX += delta.X;
        _panY += delta.Y;
        _lastDragPoint = current;
        UpdateTranslation();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndDrag();
        e.Handled = true;
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Root.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsCopyShortcut(e))
        {
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                ZoomIn();
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                ZoomOut();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0:
                FitToWindow();
                e.Handled = true;
                break;
            case Key.D1:
            case Key.NumPad1:
                ActualSize();
                e.Handled = true;
                break;
            case Key.R:
                RotateClockwise();
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