using System.Security.Cryptography;
using MiYing.Core.Crypto;

namespace MiYing.Core.Tests;

internal static class TestHelpers
{
    public static byte[] RandomData(int length)
    {
        byte[] data = new byte[length];
        RandomNumberGenerator.Fill(data);
        return data;
    }

    /// <summary>测试用最快参数：最低迭代次数 + 最小分块。</summary>
    public static MsvEncryptOptions FastOptions(string fileName = "示例 视频.mp4") => new()
    {
        Iterations = MsvConstants.MinimumIterations,
        ChunkSize = MsvConstants.MinChunkSize,
        OriginalFileName = fileName,
    };

    /// <summary>同步的进度收集器（避免 Progress&lt;T&gt; 的线程调度影响断言）。</summary>
    public sealed class SyncProgress : IProgress<MsvProgress>
    {
        private readonly object _sync = new();

        public List<MsvProgress> Reports { get; } = new();

        public Action<MsvProgress>? OnReport { get; set; }

        public void Report(MsvProgress value)
        {
            lock (_sync)
            {
                Reports.Add(value);
            }

            OnReport?.Invoke(value);
        }
    }

    /// <summary>临时目录。</summary>
    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "miying-tests-" + Guid.NewGuid().ToString("N"));

        public TempDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // 测试清理失败不影响结果
            }
        }
    }
}