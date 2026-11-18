using System.Text;
using MiYing.Core.Crypto;

namespace MiYing.Core.Naming;

/// <summary>媒体类型（由原始文件名或加密文件扩展名推断）。</summary>
public enum MediaKind
{
    Unknown,
    Video,
    Photo,
}

/// <summary>加密/还原时的文件命名规则。</summary>
public static class MsvNaming
{
    /// <summary>照片加密文件的扩展名（与视频共用 MSV 容器，仅扩展名不同）。</summary>
    public const string PhotoExtension = ".msp";

    /// <summary>工具接受加密的常见视频扩展名。</summary>
    public static readonly string[] KnownVideoExtensions =
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v", ".ts", ".flv", ".wmv", ".mpg", ".mpeg", ".3gp", ".rmvb",
    };

    /// <summary>工具接受加密的常见图片扩展名（WPF 原生可解码的格式）。</summary>
    public static readonly string[] KnownImageExtensions =
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".gif", ".tif", ".tiff", ".ico",
        ".webp", ".heic", ".heif", ".avif",
    };

    /// <summary>判断是否为工具支持加密的视频扩展名。</summary>
    public static bool IsKnownVideoExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return KnownVideoExtensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>判断是否为工具支持加密的图片扩展名。</summary>
    public static bool IsKnownImageExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return KnownImageExtensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>判断是否为密影加密文件（.msv 或 .msp）。</summary>
    public static bool IsEncryptedFile(string path)
        => path.EndsWith(MsvConstants.FileExtension, StringComparison.OrdinalIgnoreCase)
           || path.EndsWith(PhotoExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 推断媒体类型：优先看原始文件名（文件头中保存的名字），
    /// 无法判断时再根据加密文件扩展名回退。
    /// </summary>
    public static MediaKind DetectMediaKind(string? originalFileName, string? encryptedPath = null)
    {
        if (!string.IsNullOrWhiteSpace(originalFileName))
        {
            if (IsKnownImageExtension(originalFileName))
            {
                return MediaKind.Photo;
            }

            if (IsKnownVideoExtension(originalFileName))
            {
                return MediaKind.Video;
            }
        }

        if (!string.IsNullOrWhiteSpace(encryptedPath))
        {
            if (encryptedPath.EndsWith(PhotoExtension, StringComparison.OrdinalIgnoreCase))
            {
                return MediaKind.Photo;
            }

            if (encryptedPath.EndsWith(MsvConstants.FileExtension, StringComparison.OrdinalIgnoreCase))
            {
                // 原始文件名无法判断时，交给解密后的文件签名识别，避免把照片误当视频。
                return MediaKind.Unknown;
            }
        }

        return MediaKind.Unknown;
    }

    /// <summary>
    /// 生成加密输出路径：视频 <c>目录\视频.mp4</c> → <c>目录\视频.msv</c>；
    /// 照片 <c>目录\照片.jpg</c> → <c>目录\照片.msp</c>。
    /// </summary>
    public static string GetEncryptedPath(string sourcePath, string? targetDirectory = null)
    {
        string fullPath = Path.GetFullPath(sourcePath);
        string directory = targetDirectory ?? Path.GetDirectoryName(fullPath)!;
        string fileName = Path.GetFileName(fullPath);
        MediaKind kind = DetectMediaKind(fileName);
        string extension = kind == MediaKind.Photo ? PhotoExtension : MsvConstants.FileExtension;

        string baseName = fileName;
        foreach (string known in KnownVideoExtensions.Concat(KnownImageExtensions))
        {
            if (fileName.EndsWith(known, StringComparison.OrdinalIgnoreCase))
            {
                baseName = fileName[..^known.Length];
                break;
            }
        }

        return Path.Combine(directory, baseName + extension);
    }

    /// <summary>生成解密还原路径（优先恢复文件名中记录的原始文件名）。</summary>
    public static string GetRestoredPath(string encryptedPath, string originalFileName, string? targetDirectory = null)
    {
        string directory = targetDirectory ?? Path.GetDirectoryName(Path.GetFullPath(encryptedPath))!;
        string name = SanitizeFileName(originalFileName);
        if (string.IsNullOrEmpty(name))
        {
            string fallbackExtension = encryptedPath.EndsWith(PhotoExtension, StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".mp4";
            name = Path.GetFileNameWithoutExtension(encryptedPath) + fallbackExtension;
        }

        return Path.Combine(directory, name);
    }

    /// <summary>若目标路径已存在，则追加 " (n)" 序号生成不冲突的路径。</summary>
    public static string MakeUniquePath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        string directory = Path.GetDirectoryName(desiredPath)!;
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);
        for (int i = 1; i < 10_000; i++)
        {
            string candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成唯一的文件名。");
    }

    /// <summary>清理文件名中的非法字符与路径成分。</summary>
    public static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string trimmed = name.Trim();
        int lastSeparator = trimmed.LastIndexOfAny(['\\', '/']);
        string fileName = lastSeparator >= 0 ? trimmed[(lastSeparator + 1)..] : trimmed;
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (char c in fileName)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.ToString().Trim().TrimEnd('.');
    }
}