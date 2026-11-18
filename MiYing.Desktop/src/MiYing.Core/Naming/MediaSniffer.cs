namespace MiYing.Core.Naming;

/// <summary>根据解密后的文件头字节判断媒体类型（用于扩展名缺失或未知的情况）。</summary>
public static class MediaSniffer
{
    public static MediaKind Detect(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2)
        {
            return MediaKind.Unknown;
        }

        // JPEG
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return MediaKind.Photo;
        }

        // PNG
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (header.Length >= png.Length && header[..png.Length].SequenceEqual(png))
        {
            return MediaKind.Photo;
        }

        // GIF
        if (header.Length >= 6
            && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8)))
        {
            return MediaKind.Photo;
        }

        // BMP
        if (header[0] == (byte)'B' && header[1] == (byte)'M')
        {
            return MediaKind.Photo;
        }

        // TIFF (little / big endian)
        if (header.Length >= 4
            && ((header[0] == (byte)'I' && header[1] == (byte)'I' && header[2] == 0x2A && header[3] == 0)
                || (header[0] == (byte)'M' && header[1] == (byte)'M' && header[2] == 0 && header[3] == 0x2A)))
        {
            return MediaKind.Photo;
        }

        // ICO
        if (header.Length >= 4 && header[0] == 0 && header[1] == 0 && header[2] == 1 && header[3] == 0)
        {
            return MediaKind.Photo;
        }

        // WebP (RIFF....WEBP)
        if (header.Length >= 12
            && header[..4].SequenceEqual("RIFF"u8)
            && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return MediaKind.Photo;
        }

        // ISO BMFF: ftyp 容器，根据 brand 区分 HEIC / HEIF / AVIF 与 MP4 / MOV / 3GP
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            ReadOnlySpan<byte> brand = header.Slice(8, 4);
            if (brand.SequenceEqual("heic"u8)
                || brand.SequenceEqual("heix"u8)
                || brand.SequenceEqual("hevc"u8)
                || brand.SequenceEqual("hevx"u8)
                || brand.SequenceEqual("heim"u8)
                || brand.SequenceEqual("heis"u8)
                || brand.SequenceEqual("hevm"u8)
                || brand.SequenceEqual("hevs"u8)
                || brand.SequenceEqual("mif1"u8)
                || brand.SequenceEqual("msf1"u8)
                || brand.SequenceEqual("avif"u8)
                || brand.SequenceEqual("avis"u8))
            {
                return MediaKind.Photo;
            }

            // isom / mp41 / mp42 / qt / M4V / 3gp 等均按视频处理
            return MediaKind.Video;
        }

        return MediaKind.Unknown;
    }
}