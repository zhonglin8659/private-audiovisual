using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiYing.Encryptor.Services;

/// <summary>界面自绘截图（用于自动化验收，避免依赖屏幕捕获）。</summary>
internal static class QaCapture
{
    public static void CaptureWindows(string pathPrefix)
    {
        Application app = Application.Current;
        int index = 0;
        foreach (Window window in app.Windows)
        {
            try
            {
                window.UpdateLayout();
                if (window.Background is null && window is Wpf.Ui.Controls.FluentWindow)
                {
                    if (app.TryFindResource("SolidBackgroundFillColorBaseBrush") is Brush brush)
                    {
                        window.Background = brush;
                    }
                }

                int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
                int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
                if (width < 10 || height < 10)
                {
                    continue;
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                string path = $"{pathPrefix}-{window.GetType().Name}-{index}.png";
                using var stream = File.Create(path);
                encoder.Save(stream);
            }
            catch
            {
                // 截图失败不影响主流程
            }

            index++;
        }
    }
}