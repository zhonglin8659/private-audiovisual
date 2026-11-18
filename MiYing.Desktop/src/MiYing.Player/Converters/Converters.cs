using System.Globalization;
using System.Windows.Data;
using MiYing.Player.ViewModels;
using Wpf.Ui.Controls;

namespace MiYing.Player.Converters;

/// <summary>Bool 取反。</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>毫秒 → 时间文本（如 1:02:03）。</summary>
public sealed class TimeMsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long ms = value switch
        {
            long l => l,
            int i => i,
            double d => (long)d,
            _ => 0,
        };
        return PlayerViewModel.FormatTime(ms);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>空字符串 → 折叠。</summary>
public sealed class EmptyStringToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string)
            ? System.Windows.Visibility.Collapsed
            : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>播放 / 暂停按钮图标。</summary>
public sealed class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.Pause24 : SymbolRegular.Play24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>结束状态图标（重播 / 播放）。</summary>
public sealed class EndedIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.ArrowRepeatAll24 : SymbolRegular.Play24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>最近记录图标：照片 / 视频。</summary>
public sealed class RecentIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, "Photo", StringComparison.OrdinalIgnoreCase)
            ? SymbolRegular.Image24
            : SymbolRegular.LockClosed24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>音量图标。</summary>
public sealed class VolumeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double volume = value is double d ? d : 0;
        return volume switch
        {
            <= 0.5 => SymbolRegular.SpeakerMute24,
            < 34 => SymbolRegular.Speaker024,
            < 67 => SymbolRegular.Speaker124,
            _ => SymbolRegular.Speaker224,
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
/// <summary>本次列表图标：照片 / 视频。</summary>
public sealed class MediaIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, "Photo", StringComparison.OrdinalIgnoreCase)
            ? SymbolRegular.Image24
            : SymbolRegular.PlayCircle24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}