using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MiYing.Core.Naming;
using MiYing.UI;

namespace MiYing.Encryptor.ViewModels;

public enum FileItemStatus
{
    Waiting,
    Working,
    Done,
    Failed,
    Canceled,
}

/// <summary>列表中单个文件的视图模型。</summary>
public partial class FileItemViewModel : ObservableObject
{
    public FileItemViewModel(string sourcePath)
    {
        SourcePath = sourcePath;
        try
        {
            FileSize = new FileInfo(sourcePath).Length;
        }
        catch
        {
            FileSize = 0;
        }
    }

    public string SourcePath { get; }

    public string FileName => Path.GetFileName(SourcePath);

    public string DirectoryName => Path.GetDirectoryName(SourcePath) ?? string.Empty;

    public long FileSize { get; }

    public string FileSizeText => FormatSize(FileSize);

    /// <summary>是否为图片（照片加密模式下用于缩略图与勾选）。</summary>
    public bool IsPhoto => MsvNaming.IsKnownImageExtension(SourcePath);

    [ObservableProperty]
    private FileItemStatus _status = FileItemStatus.Waiting;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _statusText = "等待中";

    [ObservableProperty]
    private string _targetPath = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>照片是否参与本次加密（视频/解密模式始终为 true）。</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    [ObservableProperty]
    private bool _thumbnailFailed;

    /// <summary>异步加载缩略图；由主视图模型限制并发数。</summary>
    public async Task LoadThumbnailAsync(SemaphoreSlim gate)
    {
        if (!IsPhoto || Thumbnail is not null)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(true);
        try
        {
            string path = SourcePath;
            ImageSource image = await Task.Run(() => PhotoImageLoader.LoadThumbnail(path, 320)).ConfigureAwait(true);
            Thumbnail = image;
        }
        catch
        {
            ThumbnailFailed = true;
        }
        finally
        {
            gate.Release();
        }
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes} B"
            : $"{value:0.##} {units[unit]}";
    }
}