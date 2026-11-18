using System.IO;
using System.Windows;
using MiYing.Core.Crypto;
using MiYing.Encryptor.Services;
using Wpf.Ui.Appearance;

namespace MiYing.Encryptor;

public partial class App : Application
{
    public static AppThemeMode CurrentThemeMode { get; private set; } = AppThemeMode.System;

    /// <summary>截图模式输出前缀（QA 用）。</summary>
    public static string? ScreenshotPath { get; private set; }

    /// <summary>截图模式下预先加入列表的文件（QA 用）。</summary>
    public static List<string> DemoFiles { get; } = new();

    /// <summary>截图模式下直接进入照片加密模式（QA 用）。</summary>
    public static bool PhotoDemo { get; private set; }

    /// <summary>截图模式下同时打开照片预览窗口（QA 用）。</summary>
    public static string? PreviewDemoFile { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 备选开关：若个别机器上窗口显示异常，可设置环境变量 MIYING_SOFTWARE_RENDER=1 强制软件渲染
        if (Environment.GetEnvironmentVariable("MIYING_SOFTWARE_RENDER") == "1")
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        // 命令行自动化：--encrypt <输入> <输出> <密码> / --decrypt <输入> <输出> <密码>
        if (e.Args.Length >= 4 && (e.Args[0] == "--encrypt" || e.Args[0] == "--decrypt"))
        {
            Environment.Exit(RunCli(e.Args));
            return;
        }

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--screenshot" && i + 1 < e.Args.Length)
            {
                ScreenshotPath = e.Args[i + 1];
            }
            else if (e.Args[i] == "--add" && i + 1 < e.Args.Length)
            {
                DemoFiles.Add(e.Args[i + 1]);
            }
            else if (e.Args[i] == "--photo")
            {
                PhotoDemo = true;
            }
            else if (e.Args[i] == "--preview" && i + 1 < e.Args.Length)
            {
                PreviewDemoFile = e.Args[i + 1];
            }
        }

        CurrentThemeMode = AppSettings.Load().Theme;
        ApplyTheme(CurrentThemeMode, persist: false);
    }

    public static void ApplyTheme(AppThemeMode mode, bool persist = true)
    {
        CurrentThemeMode = mode;

        switch (mode)
        {
            case AppThemeMode.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppThemeMode.Dark:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }

        ApplyBrandAccent();

        if (persist)
        {
            AppSettings settings = AppSettings.Load();
            settings.Theme = mode;
            settings.Save();
        }
    }

    private static int RunCli(string[] args)
    {
        string logPath = Path.Combine(Path.GetTempPath(), "miying-cli.log");
        try
        {
            string inputPath = args[1];
            string outputPath = args[2];
            string password = args[3];

            if (args[0] == "--encrypt")
            {
                var options = new MsvEncryptOptions
                {
                    Iterations = MsvConstants.DefaultIterations,
                    ChunkSize = MsvConstants.DefaultChunkSize,
                    OriginalFileName = Path.GetFileName(inputPath),
                    VerifyAfterEncrypt = true,
                };
                VideoCipher.EncryptFileAsync(inputPath, outputPath, password, options).GetAwaiter().GetResult();
            }
            else
            {
                VideoCipher.DecryptFileAsync(inputPath, outputPath, password).GetAwaiter().GetResult();
            }

            File.WriteAllText(logPath, "OK " + args[0] + " -> " + outputPath);
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(logPath, "FAIL " + ex);
            return 1;
        }
    }

    /// <summary>应用密影品牌主色（靛蓝）。</summary>
    private static void ApplyBrandAccent()
    {
        try
        {
            var primary = System.Windows.Media.Color.FromRgb(0x4C, 0x5B, 0xE0);
            var secondary = System.Windows.Media.Color.FromRgb(0x7C, 0x4D, 0xE8);
            var tertiary = System.Windows.Media.Color.FromRgb(0xA5, 0x7C, 0xF0);
            ApplicationAccentColorManager.Apply(primary, primary, secondary, tertiary);
        }
        catch
        {
            // 主色设置失败不影响功能
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        try
        {
            string logPath = Path.Combine(Path.GetTempPath(), "miying-crash.log");
            File.WriteAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + args.Exception);
        }
        catch
        {
            // 日志写入失败时忽略
        }

        MessageBox.Show(
            "程序出现未处理的错误：\n\n" + args.Exception.Message,
            "密影",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        args.Handled = true;
    }
}