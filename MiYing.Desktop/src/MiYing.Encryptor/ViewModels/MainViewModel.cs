using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MiYing.Core.Crypto;
using MiYing.Core.Naming;
using MiYing.Encryptor.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace MiYing.Encryptor.ViewModels;

public enum WorkMode
{
    Encrypt,
    PhotoEncrypt,
    Decrypt,
}

/// <summary>主界面的视图模型：文件队列管理与批量加解密调度。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly SemaphoreSlim _thumbnailGate = new(4);
    private CancellationTokenSource? _cts;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();

    // ---------- 模式 ----------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEncryptMode))]
    [NotifyPropertyChangedFor(nameof(IsVideoMode))]
    [NotifyPropertyChangedFor(nameof(IsPhotoMode))]
    [NotifyPropertyChangedFor(nameof(IsDecryptMode))]
    [NotifyPropertyChangedFor(nameof(IsListMode))]
    [NotifyPropertyChangedFor(nameof(ModeTitle))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    [NotifyPropertyChangedFor(nameof(StartButtonText))]
    [NotifyPropertyChangedFor(nameof(AddFilesButtonText))]
    [NotifyPropertyChangedFor(nameof(EmptyStateTitle))]
    [NotifyPropertyChangedFor(nameof(EmptyStateDescription))]
    private WorkMode _mode = WorkMode.Encrypt;

    public bool IsEncryptMode => Mode != WorkMode.Decrypt;

    public bool IsVideoMode => Mode == WorkMode.Encrypt;

    public bool IsPhotoMode => Mode == WorkMode.PhotoEncrypt;

    public bool IsDecryptMode => Mode == WorkMode.Decrypt;

    public bool IsListMode => !IsPhotoMode;

    public string ModeTitle => Mode switch
    {
        WorkMode.PhotoEncrypt => "照片加密",
        WorkMode.Decrypt => "解密还原",
        _ => "视频加密",
    };

    public string ModeDescription => Mode switch
    {
        WorkMode.PhotoEncrypt => "将照片加密为 .msp 文件 —— 只有「密影播放器」输入正确密码后才能查看",
        WorkMode.Decrypt => "将 .msv / .msp 加密文件还原为原始视频或照片",
        _ => "将视频加密为 .msv 文件 —— 只有「密影播放器」输入正确密码后才能观看",
    };

    public string StartButtonText => Mode switch
    {
        WorkMode.PhotoEncrypt => "开始加密照片",
        WorkMode.Decrypt => "开始解密",
        _ => "开始加密",
    };

    public string AddFilesButtonText => Mode switch
    {
        WorkMode.PhotoEncrypt => "添加照片",
        WorkMode.Decrypt => "添加文件",
        _ => "添加视频",
    };

    public string EmptyStateTitle => Mode == WorkMode.Decrypt
        ? "拖拽加密文件到此处"
        : "拖拽视频到此处";

    public string EmptyStateDescription => Mode == WorkMode.Decrypt
        ? "或通过下方按钮添加（支持 .msv / .msp）"
        : "或通过下方按钮添加（支持 MP4 / MOV / MKV / AVI 等格式）";

    // ---------- 密码 ----------

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private int _passwordScore;

    [ObservableProperty]
    private string _passwordScoreText = string.Empty;

    partial void OnPasswordChanged(string value)
    {
        PasswordScore = EvaluateStrength(value);
        PasswordScoreText = PasswordScore switch
        {
            0 => string.Empty,
            1 => "弱",
            2 => "中",
            3 => "强",
            _ => "极强",
        };
        StartCommand.NotifyCanExecuteChanged();
    }

    /// <summary>粗略的密码强度评估（0~4）。</summary>
    public static int EvaluateStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return 0;
        }

        int classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        int bits = password.Length * (2 + classes);
        return bits switch
        {
            < 28 => 1,
            < 44 => 2,
            < 66 => 3,
            _ => 4,
        };
    }

    // ---------- 选项 ----------

    [ObservableProperty]
    private bool _verifyAfterEncrypt = true;

    [ObservableProperty]
    private bool _deleteOriginalAfter;

    /// <summary>0 = 标准（60 万次迭代），1 = 增强（120 万次迭代）。</summary>
    [ObservableProperty]
    private int _strengthIndex;

    [ObservableProperty]
    private bool _useCustomOutputFolder;

    [ObservableProperty]
    private string _outputFolder = string.Empty;

    // ---------- 运行状态 ----------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallText = "就绪";

    [ObservableProperty]
    private bool _hasFiles;

    [ObservableProperty]
    private bool _noFiles = true;

    [ObservableProperty]
    private bool _isSummaryOpen;

    [ObservableProperty]
    private string _summaryMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _summarySeverity = InfoBarSeverity.Informational;

    // ---------- 照片选择 ----------

    public int SelectedPhotoCount => Files.Count(f => f.IsSelected);

    public string PhotoSelectionText => $"已选 {SelectedPhotoCount} / {Files.Count} 张";

    // ---------- 文件管理 ----------

    [RelayCommand]
    private void AddFiles()
    {
        if (IsBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = Mode switch
            {
                WorkMode.PhotoEncrypt => "选择要加密的照片",
                WorkMode.Decrypt => "选择要解密的 .msv / .msp 文件",
                _ => "选择要加密的视频",
            },
            Multiselect = true,
            Filter = Mode switch
            {
                WorkMode.PhotoEncrypt => "图片文件|*.jpg;*.jpeg;*.jpe;*.jfif;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.webp;*.heic;*.heif;*.avif|所有文件|*.*",
                WorkMode.Decrypt => "密影加密文件 (*.msv;*.msp)|*.msv;*.msp|所有文件|*.*",
                _ => "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm;*.ts;*.flv;*.wmv;*.mpg;*.mpeg;*.3gp;*.rmvb|所有文件|*.*",
            },
        };

        if (dialog.ShowDialog() == true)
        {
            AddPaths(dialog.FileNames);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        if (IsBusy)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = Mode switch
            {
                WorkMode.PhotoEncrypt => "选择包含照片的文件夹",
                WorkMode.Decrypt => "选择包含 .msv / .msp 文件的文件夹",
                _ => "选择包含视频的文件夹",
            },
        };

        if (dialog.ShowDialog() == true)
        {
            AddPaths(new[] { dialog.FolderName });
        }
    }

    [RelayCommand]
    private void RemoveFile(FileItemViewModel? item)
    {
        if (IsBusy || item is null)
        {
            return;
        }

        Files.Remove(item);
        UpdateFileState();
    }

    [RelayCommand]
    private void ClearFiles()
    {
        if (IsBusy)
        {
            return;
        }

        Files.Clear();
        UpdateFileState();
        IsSummaryOpen = false;
    }

    [RelayCommand]
    private void SelectAllPhotos()
    {
        if (IsBusy || !IsPhotoMode)
        {
            return;
        }

        foreach (FileItemViewModel item in Files)
        {
            item.IsSelected = true;
        }

        UpdateSelectionState();
    }

    [RelayCommand]
    private void InvertPhotoSelection()
    {
        if (IsBusy || !IsPhotoMode)
        {
            return;
        }

        foreach (FileItemViewModel item in Files)
        {
            item.IsSelected = !item.IsSelected;
        }

        UpdateSelectionState();
    }

    /// <summary>添加文件或文件夹（自动递归展开目录并过滤、去重）。</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        if (IsBusy)
        {
            return;
        }

        var existing = new HashSet<string>(Files.Select(f => f.SourcePath), StringComparer.OrdinalIgnoreCase);
        var pending = new List<string>();

        foreach (string raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw);
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(full))
            {
                try
                {
                    pending.AddRange(Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories));
                }
                catch
                {
                    // 忽略无权限的目录
                }
            }
            else if (File.Exists(full))
            {
                pending.Add(full);
            }
        }

        int added = 0;
        var addedItems = new List<FileItemViewModel>();
        foreach (string file in pending)
        {
            if (!IsFileValidForMode(file) || !existing.Add(file))
            {
                continue;
            }

            var item = new FileItemViewModel(file);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FileItemViewModel.Status)
                    || e.PropertyName == nameof(FileItemViewModel.IsSelected))
                {
                    StartCommand.NotifyCanExecuteChanged();
                    UpdateSelectionState();
                }
            };
            Files.Add(item);
            addedItems.Add(item);
            added++;
        }

        UpdateFileState();

        foreach (FileItemViewModel item in addedItems)
        {
            _ = item.LoadThumbnailAsync(_thumbnailGate);
        }

        if (added == 0)
        {
            ShowSummary(
                Mode switch
                {
                    WorkMode.PhotoEncrypt => "未添加照片：请选择 JPG / PNG / WebP / HEIC / TIFF 等图片格式。",
                    WorkMode.Decrypt => "未添加文件：请选择 .msv 或 .msp 加密文件。",
                    _ => "未添加文件：请选择 MP4 / MOV / MKV 等常见视频格式。",
                },
                InfoBarSeverity.Warning);
        }
    }

    private bool IsFileValidForMode(string path) => Mode switch
    {
        WorkMode.PhotoEncrypt => MsvNaming.IsKnownImageExtension(path),
        WorkMode.Decrypt => MsvNaming.IsEncryptedFile(path),
        _ => MsvNaming.IsKnownVideoExtension(path),
    };

    private void UpdateFileState()
    {
        HasFiles = Files.Count > 0;
        NoFiles = !HasFiles;
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        OnPropertyChanged(nameof(SelectedPhotoCount));
        OnPropertyChanged(nameof(PhotoSelectionText));
        StartCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SwitchMode(string? mode)
    {
        if (IsBusy)
        {
            return;
        }

        WorkMode target = mode switch
        {
            "Decrypt" => WorkMode.Decrypt,
            "PhotoEncrypt" => WorkMode.PhotoEncrypt,
            _ => WorkMode.Encrypt,
        };

        if (target == Mode)
        {
            return;
        }

        Files.Clear();
        Mode = target;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        UseCustomOutputFolder = false;
        OutputFolder = string.Empty;
        IsSummaryOpen = false;
        UpdateFileState();
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择输出文件夹" };
        if (!string.IsNullOrEmpty(OutputFolder) && Directory.Exists(OutputFolder))
        {
            dialog.InitialDirectory = OutputFolder;
        }

        if (dialog.ShowDialog() == true)
        {
            OutputFolder = dialog.FolderName;
        }
    }

    // ---------- 开始 / 取消 ----------

    private bool CanStart()
        => !IsBusy
           && Files.Count > 0
           && !string.IsNullOrEmpty(Password)
           && Files.Any(f => f.IsSelected && f.Status is FileItemStatus.Waiting or FileItemStatus.Failed or FileItemStatus.Canceled);

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>请求取消当前批量任务（供窗口关闭流程使用）。</summary>
    public void CancelCurrent() => _cts?.Cancel();

    /// <summary>等待当前批量任务结束（供窗口关闭流程使用）。</summary>
    public async Task WaitForIdleAsync()
    {
        if (StartCommand.ExecutionTask is Task task)
        {
            try
            {
                await task;
            }
            catch
            {
                // 关闭流程中忽略任务异常
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (!ValidateInput(out string error))
        {
            ShowSummary(error, InfoBarSeverity.Warning);
            return;
        }

        if (IsEncryptMode && PasswordScore <= 1)
        {
            var result = await new MessageBox
            {
                Title = "密码强度较弱",
                Content = "当前密码较短或过于简单，容易被暴力破解。\n建议使用 12 位以上的大小写字母、数字与符号组合。\n\n要使用当前密码继续加密吗？",
                PrimaryButtonText = "继续加密",
                CloseButtonText = "返回修改",
            }.ShowDialogAsync();

            if (result != MessageBoxResult.Primary)
            {
                return;
            }
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        IsSummaryOpen = false;

        int total = Files.Count(f => f.IsSelected && f.Status == FileItemStatus.Waiting);
        int index = 0, ok = 0, failed = 0, canceledCount = 0;
        string verb = IsEncryptMode ? "加密" : "解密";

        try
        {
            foreach (FileItemViewModel item in Files.ToList())
            {
                if (!item.IsSelected || item.Status != FileItemStatus.Waiting)
                {
                    continue;
                }

                index++;

                if (_cts.IsCancellationRequested)
                {
                    item.Status = FileItemStatus.Canceled;
                    item.StatusText = "已取消";
                    canceledCount++;
                    continue;
                }

                item.Progress = 0;
                item.ErrorMessage = string.Empty;
                item.Status = FileItemStatus.Working;
                item.StatusText = IsEncryptMode ? "准备加密…" : "准备解密…";
                OverallText = $"{verb}中 {index}/{total} · {item.FileName}";

                int current = index;
                var progress = new Progress<MsvProgress>(p =>
                {
                    item.Progress = p.Fraction;
                    item.StatusText = StageText(p.Stage, p.Fraction);
                    OverallProgress = ((current - 1) + p.Fraction) / Math.Max(1, total);
                });

                try
                {
                    if (IsEncryptMode)
                    {
                        await ProcessEncryptAsync(item, progress, _cts.Token);
                    }
                    else
                    {
                        await ProcessDecryptAsync(item, progress, _cts.Token);
                    }

                    item.Progress = 1;
                    item.Status = FileItemStatus.Done;
                    if (string.IsNullOrEmpty(item.StatusText) || !item.StatusText.Contains("原文件"))
                    {
                        item.StatusText = "已完成";
                    }
                    ok++;
                }
                catch (OperationCanceledException)
                {
                    item.Status = FileItemStatus.Canceled;
                    item.StatusText = "已取消";
                    canceledCount++;
                }
                catch (Exception ex)
                {
                    item.Status = FileItemStatus.Failed;
                    item.StatusText = "失败";
                    item.ErrorMessage = ex.Message;
                    failed++;
                }

                OverallProgress = (double)index / Math.Max(1, total);
            }
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            OverallText = "就绪";
            OverallProgress = 0;
        }

        string unit = IsPhotoMode ? " 张照片" : " 个文件";
        if (failed == 0 && canceledCount == 0)
        {
            ShowSummary($"{verb}完成：成功处理 {ok}{unit}。", InfoBarSeverity.Success);
        }
        else if (ok == 0 && failed == 0)
        {
            ShowSummary("操作已取消。", InfoBarSeverity.Informational);
        }
        else
        {
            ShowSummary(
                $"{verb}结束：成功 {ok}{unit}，失败 {failed}{unit}，取消 {canceledCount}{unit}。失败原因见列表中对应文件的提示。",
                failed > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Informational);
        }
    }

    private bool ValidateInput(out string error)
    {
        error = string.Empty;

        if (Files.Count == 0)
        {
            error = "请先添加要处理的文件。";
            return false;
        }

        if (IsPhotoMode && !Files.Any(f => f.IsSelected && f.Status == FileItemStatus.Waiting))
        {
            error = "请至少选择一张要加密的照片。";
            return false;
        }

        if (string.IsNullOrEmpty(Password))
        {
            error = "请输入密码。";
            return false;
        }

        if (IsEncryptMode)
        {
            if (Password.Length < 8)
            {
                error = "密码至少需要 8 个字符。";
                return false;
            }

            if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
            {
                error = "两次输入的密码不一致。";
                return false;
            }

            if (UseCustomOutputFolder && string.IsNullOrWhiteSpace(OutputFolder))
            {
                error = "请选择输出文件夹。";
                return false;
            }
        }

        return true;
    }

    private async Task ProcessEncryptAsync(FileItemViewModel item, IProgress<MsvProgress> progress, CancellationToken ct)
    {
        string? targetDir = UseCustomOutputFolder && !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : null;
        string target = MsvNaming.MakeUniquePath(MsvNaming.GetEncryptedPath(item.SourcePath, targetDir));

        var options = new MsvEncryptOptions
        {
            Iterations = StrengthIndex == 1 ? 1_200_000 : MsvConstants.DefaultIterations,
            ChunkSize = MsvConstants.DefaultChunkSize,
            OriginalFileName = Path.GetFileName(item.SourcePath),
            VerifyAfterEncrypt = VerifyAfterEncrypt,
        };

        await VideoCipher.EncryptFileAsync(item.SourcePath, target, Password, options, progress, ct);
        item.TargetPath = target;
        await TryDeleteOriginalAsync(item, ct);
    }

    private async Task ProcessDecryptAsync(FileItemViewModel item, IProgress<MsvProgress> progress, CancellationToken ct)
    {
        MsvHeaderInfo info = MsvVideo.ReadHeader(item.SourcePath);
        string? targetDir = UseCustomOutputFolder && !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : null;
        string target = MsvNaming.MakeUniquePath(
            MsvNaming.GetRestoredPath(item.SourcePath, info.Header.OriginalFileName, targetDir));

        await VideoCipher.DecryptFileAsync(item.SourcePath, target, Password, progress, ct);
        item.TargetPath = target;
        await TryDeleteOriginalAsync(item, ct);
    }

    private async Task TryDeleteOriginalAsync(FileItemViewModel item, CancellationToken ct)
    {
        if (!DeleteOriginalAfter)
        {
            return;
        }

        string source = item.SourcePath;
        bool moved = await Task.Run(() => Services.RecycleBin.TryMoveToRecycleBin(source, out _), ct);
        if (!moved)
        {
            item.StatusText = "已完成（原文件删除失败，已保留）";
        }
        else
        {
            item.StatusText = "已完成（原文件已移入回收站）";
        }
    }

    private static string StageText(MsvStage stage, double fraction) => stage switch
    {
        MsvStage.DeriveKey => "正在派生密钥…",
        MsvStage.Encrypt => $"正在加密 {fraction * 100:0}%",
        MsvStage.Decrypt => $"正在解密 {fraction * 100:0}%",
        MsvStage.Verify => $"正在校验完整性 {fraction * 100:0}%",
        _ => "处理中…",
    };

    private void ShowSummary(string message, InfoBarSeverity severity)
    {
        SummaryMessage = message;
        SummarySeverity = severity;
        IsSummaryOpen = true;
    }
}