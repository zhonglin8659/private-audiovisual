using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MiYing.UI;

/// <summary>照片加载结果（图像已 Freeze，可跨线程使用）。</summary>
public sealed record PhotoLoadResult(
    ImageSource Image,
    int PixelWidth,
    int PixelHeight,
    string? CameraMake,
    string? CameraModel,
    DateTime? TakenAt);

/// <summary>
/// 从明文文件或密影解密流加载照片。解码在内存中完成，不产生临时文件。
/// </summary>
public static class PhotoImageLoader
{
    private const int DefaultMaxPixelWidth = 6000;

    private readonly record struct ImageMetadata(
        int Orientation,
        string? Make,
        string? Model,
        DateTime? Taken,
        int SourceWidth,
        int SourceHeight);

    /// <summary>加载明文图片文件。</summary>
    public static PhotoLoadResult Load(string path, int maxPixelWidth = DefaultMaxPixelWidth)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        return Load(stream, maxPixelWidth);
    }

    /// <summary>加载明文图片文件的缩略图。</summary>
    public static ImageSource LoadThumbnail(string path, int maxPixelWidth = 320)
        => Load(path, maxPixelWidth).Image;

    /// <summary>从可定位流加载照片（不负责关闭流）。</summary>
    public static PhotoLoadResult Load(Stream stream, int maxPixelWidth = DefaultMaxPixelWidth)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("图片流必须可读且支持定位。", nameof(stream));
        }

        ImageMetadata metadata = ReadMetadata(stream);
        stream.Position = 0;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;

            // 只缩小、不放大：避免把 4000px 的照片解码成 6000px。
            if (maxPixelWidth > 0 && metadata.SourceWidth > maxPixelWidth)
            {
                image.DecodePixelWidth = maxPixelWidth;
            }

            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            bool swap = metadata.Orientation is 5 or 6 or 7 or 8;
            int width = swap ? image.PixelHeight : image.PixelWidth;
            int height = swap ? image.PixelWidth : image.PixelHeight;

            ImageSource result = ApplyOrientation(image, metadata.Orientation);
            return new PhotoLoadResult(result, width, height, metadata.Make, metadata.Model, metadata.Taken);
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new NotSupportedException(
                "当前系统无法解码该图片格式（可直接预览 JPG / PNG / BMP / GIF / TIFF）。",
                ex);
        }
    }

    private static ImageMetadata ReadMetadata(Stream stream)
    {
        try
        {
            stream.Position = 0;
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.None);

            if (decoder.Frames.Count == 0)
            {
                return new ImageMetadata(1, null, null, null, 0, 0);
            }

            BitmapFrame frame = decoder.Frames[0];
            int sourceWidth = frame.PixelWidth;
            int sourceHeight = frame.PixelHeight;

            if (frame.Metadata is not BitmapMetadata metadata)
            {
                return new ImageMetadata(1, null, null, null, sourceWidth, sourceHeight);
            }

            int orientation = 1;
            if (TryGetQuery(metadata, "/app1/ifd/{ushort=274}") is object rawOrientation)
            {
                orientation = Convert.ToInt32(rawOrientation, CultureInfo.InvariantCulture);
            }

            string? make = TryGetQuery(metadata, "/app1/ifd/{ushort=271}") as string;
            string? model = TryGetQuery(metadata, "/app1/ifd/{ushort=272}") as string;

            DateTime? taken = null;
            if (TryGetQuery(metadata, "/app1/ifd/exif/{ushort=36867}") is string dateText
                && DateTime.TryParseExact(
                    dateText,
                    "yyyy:MM:dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out DateTime parsed))
            {
                taken = parsed;
            }

            if (orientation is < 1 or > 8)
            {
                orientation = 1;
            }

            return new ImageMetadata(orientation, make, model, taken, sourceWidth, sourceHeight);
        }
        catch
        {
            return new ImageMetadata(1, null, null, null, 0, 0);
        }
    }

    private static object? TryGetQuery(BitmapMetadata metadata, string query)
    {
        try
        {
            return metadata.GetQuery(query);
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform is null)
        {
            return source;
        }

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }
}