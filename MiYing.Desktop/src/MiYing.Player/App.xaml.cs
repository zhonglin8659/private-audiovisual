using System.IO;
using System.Windows;
using LibVLCSharp.Shared;
using Wpf.Ui.Appearance;

namespace MiYing.Player;

public partial class App : Application
{
    /// <summary>命令行传入的待打开文件（双击 .msv / .msp 关联启动时使用）。</summary>
    public static string? StartupFilePath { get; private set; }

    /// <summary>截图模式输出前缀（QA 用）。</summary>
    public static string? ScreenshotPath { get; private set; }

    /// <summary>播放自检模式：自动解锁并播放指定文件（QA 用，密码从环境变量 MIYING_SELFTEST_PASSWORD 读取）。</summary>
    public static string? SelfTestPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 备选开关：若个别机器上窗口显示异常，可设置环境变量 MIYING_SOFTWARE_RENDER=1 强制软件渲染
        if (Environment.GetEnvironmentVariable("MIYING_SOFTWARE_RENDER") == "1")
        {
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        try
        {
            string? libVlcDirectory = FindLibVlcDirectory();
            if (libVlcDirectory is not null)
            {
                LibVLCSharp.Shared.Core.Initialize(libVlcDirectory);
            }
            else
            {
                LibVLCSharp.Shared.Core.Initialize();
            }
        }
        catch (Exception ex)
        {
            bool quiet = e.Args.Contains("--selftest") || e.Args.Contains("--screenshot");
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "miying-crash.log"), "LibVLC 初始化失败\n" + ex);
            }
            catch
            {
                // 忽略
            }

            if (quiet)
            {
                Shutdown(2);
                return;
            }

            MessageBox.Show(
                "播放内核初始化失败，无法启动播放器。\n\n" + ex.Message,
                "密影播放器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        for (int i = 0; i < e.Args.Length; i++)
        {
            if (e.Args[i] == "--screenshot" && i + 1 < e.Args.Length)
            {
                ScreenshotPath = e.Args[i + 1];
            }
            else if (e.Args[i] == "--selftest" && i + 1 < e.Args.Length)
            {
                SelfTestPath = e.Args[i + 1];
            }
        }

        StartupFilePath = e.Args.FirstOrDefault(a =>
                a.EndsWith(".msv", StringComparison.OrdinalIgnoreCase)
                || a.EndsWith(".msp", StringComparison.OrdinalIgnoreCase));
        ApplyBrandAccent();
    }

    /// <summary>应用密影品牌主色（青蓝）。</summary>
    private static void ApplyBrandAccent()
    {
        try
        {
            var primary = System.Windows.Media.Color.FromRgb(0x0E, 0x6E, 0xC9);
            var secondary = System.Windows.Media.Color.FromRgb(0x19, 0x9B, 0xC9);
            var tertiary = System.Windows.Media.Color.FromRgb(0x4F, 0xC3, 0xE8);
            ApplicationAccentColorManager.Apply(primary, primary, secondary, tertiary);
        }
        catch
        {
            // 主色设置失败不影响功能
        }
    }

    /// <summary>定位 libvlc 原生库目录（兼容“文件夹发布”与“单文件发布”两种形态）。</summary>
    private static string? FindLibVlcDirectory()
    {
        try
        {
            // 1) 文件夹发布：可执行文件旁的 libvlc\win-x64
            string baseDirectory = AppContext.BaseDirectory;
            foreach (string arch in new[] { "win-x64", "win-x86" })
            {
                string candidate = Path.Combine(baseDirectory, "libvlc", arch);
                if (File.Exists(Path.Combine(candidate, "libvlc.dll")))
                {
                    return candidate;
                }
            }

            // 2) 单文件发布：运行时会将内容解压到 <解压根>\MiYing.Player*\[子目录]\libvlc\win-x64
            string extractRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR")
                ?? Path.Combine(Path.GetTempPath(), ".net");

            if (Directory.Exists(extractRoot))
            {
                foreach (string appRoot in Directory.EnumerateDirectories(extractRoot, "MiYing.Player*"))
                {
                    var searchDirs = new List<string> { appRoot };
                    try
                    {
                        searchDirs.AddRange(Directory.EnumerateDirectories(appRoot));
                    }
                    catch
                    {
                        // 忽略枚举异常
                    }

                    foreach (string dir in searchDirs)
                    {
                        foreach (string arch in new[] { "win-x64", "win-x86" })
                        {
                            string candidate = Path.Combine(dir, "libvlc", arch);
                            if (File.Exists(Path.Combine(candidate, "libvlc.dll")))
                            {
                                return candidate;
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // 查找失败则回退到默认探测逻辑
        }

        return null;
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        try
        {
            string logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "miying-crash.log");
            System.IO.File.WriteAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + args.Exception);
        }
        catch
        {
            // 日志写入失败时忽略
        }

        MessageBox.Show(
            "程序出现未处理的错误：\n\n" + args.Exception.Message,
            "密影播放器",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        args.Handled = true;
    }
}