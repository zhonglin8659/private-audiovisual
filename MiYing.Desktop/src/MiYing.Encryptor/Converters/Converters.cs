using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MiYing.Encryptor.Converters;

/// <summary>Bool 取反。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>根据文件扩展名选择图标。</summary>
public sealed class FileIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string path = value as string ?? string.Empty;
        return path.EndsWith(".msv", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".msp", StringComparison.OrdinalIgnoreCase)
            ? SymbolRegular.LockClosed24
            : SymbolRegular.VideoClip24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>视频加密 / 照片加密 / 解密模式对应按钮图标。</summary>
public sealed class ModeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is MiYing.Encryptor.ViewModels.WorkMode.PhotoEncrypt
            ? SymbolRegular.Image24
            : value is MiYing.Encryptor.ViewModels.WorkMode.Decrypt
                ? SymbolRegular.LockOpen24
                : SymbolRegular.LockClosed24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>密码强度分段色：score(0~4) + 段序号(0~3) → 画刷。</summary>
public sealed class ScoreSegmentBrushConverter : IValueConverter
{
    private static readonly Brush Track = new SolidColorBrush(Color.FromArgb(0x33, 0x80, 0x80, 0x80));
    private static readonly Brush Weak = new SolidColorBrush(Color.FromRgb(0xE8, 0x11, 0x23));
    private static readonly Brush Medium = new SolidColorBrush(Color.FromRgb(0xCA, 0x50, 0x10));
    private static readonly Brush Strong = new SolidColorBrush(Color.FromRgb(0x0F, 0x7B, 0x0F));

    static ScoreSegmentBrushConverter()
    {
        Track.Freeze();
        Weak.Freeze();
        Medium.Freeze();
        Strong.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int score = value is int i ? i : 0;
        int segment = parameter is string s && int.TryParse(s, out int p) ? p : 0;
        if (score <= 0 || segment >= score)
        {
            return Track;
        }

        return score switch
        {
            1 => Weak,
            2 => Medium,
            _ => Strong,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}