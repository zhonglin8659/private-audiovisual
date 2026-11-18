using MiYing.Core.Crypto;
using Xunit;

namespace MiYing.Core.Tests;

public class RoundTripTests
{
    private const string Password = "密影测试-Str0ng!密码#2026";

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(200_000)]
    public async Task EncryptThenDecrypt_RoundTripsExactly(int size)
    {
        byte[] data = TestHelpers.RandomData(size);
        using var source = new MemoryStream(data);
        using var encrypted = new MemoryStream();

        MsvHeader header = await VideoCipher.EncryptAsync(source, encrypted, Password, TestHelpers.FastOptions());

        Assert.Equal(size, header.PlaintextLength);
        Assert.Equal("示例 视频.mp4", header.OriginalFileName);
        Assert.Equal(header.TotalLength, encrypted.Length);

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        await VideoCipher.DecryptAsync(encrypted, decrypted, Password);

        Assert.Equal(data, decrypted.ToArray());
    }

    [Fact]
    public async Task WrongPassword_ThrowsInvalidPassword()
    {
        using var source = new MemoryStream(TestHelpers.RandomData(3000));
        using var encrypted = new MemoryStream();
        await VideoCipher.EncryptAsync(source, encrypted, Password, TestHelpers.FastOptions());

        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        await Assert.ThrowsAsync<MsvInvalidPasswordException>(
            () => VideoCipher.DecryptAsync(encrypted, decrypted, "错误密码"));
    }

    [Fact]
    public async Task FileLevel_EncryptVerifyDecrypt_Works()
    {
        using var temp = new TestHelpers.TempDirectory();
        string sourcePath = temp.File("video.mp4");
        await File.WriteAllBytesAsync(sourcePath, TestHelpers.RandomData(150_000));

        string targetPath = temp.File("video.msv");
        var options = new MsvEncryptOptions
        {
            Iterations = MsvConstants.MinimumIterations,
            ChunkSize = MsvConstants.MinChunkSize,
            OriginalFileName = "video.mp4",
            VerifyAfterEncrypt = true,
        };

        await VideoCipher.EncryptFileAsync(sourcePath, targetPath, Password, options);

        Assert.True(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".part"));
        Assert.True(await VideoCipher.VerifyEncryptedFileAsync(targetPath, Password));
        Assert.False(await VideoCipher.VerifyEncryptedFileAsync(targetPath, "错误密码"));

        string restoredPath = temp.File("restored.mp4");
        await VideoCipher.DecryptFileAsync(targetPath, restoredPath, Password);

        Assert.Equal(await File.ReadAllBytesAsync(sourcePath), await File.ReadAllBytesAsync(restoredPath));
    }

    [Fact]
    public async Task Progress_IsReportedForAllStages()
    {
        var progress = new TestHelpers.SyncProgress();
        using var source = new MemoryStream(TestHelpers.RandomData(200_000));
        using var encrypted = new MemoryStream();

        await VideoCipher.EncryptAsync(source, encrypted, Password, TestHelpers.FastOptions(), progress);
        encrypted.Position = 0;
        using var decrypted = new MemoryStream();
        await VideoCipher.DecryptAsync(encrypted, decrypted, Password, progress);

        Assert.Contains(progress.Reports, r => r.Stage == MsvStage.DeriveKey);
        Assert.Contains(progress.Reports, r => r.Stage == MsvStage.Encrypt && r.Fraction >= 1.0);
        Assert.Contains(progress.Reports, r => r.Stage == MsvStage.Decrypt && r.Fraction >= 1.0);
    }

    [Fact]
    public async Task Cancellation_LeavesNoOutputOrPartFile()
    {
        using var temp = new TestHelpers.TempDirectory();
        string sourcePath = temp.File("big.mp4");
        await File.WriteAllBytesAsync(sourcePath, TestHelpers.RandomData(2_000_000));
        string targetPath = temp.File("big.msv");

        using var cts = new CancellationTokenSource();
        var progress = new TestHelpers.SyncProgress
        {
            OnReport = p =>
            {
                if (p.Stage == MsvStage.Encrypt && p.Fraction > 0.1)
                {
                    cts.Cancel();
                }
            },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => VideoCipher.EncryptFileAsync(sourcePath, targetPath, Password, TestHelpers.FastOptions(), progress, cts.Token));

        Assert.False(File.Exists(targetPath));
        Assert.False(File.Exists(targetPath + ".part"));
    }
}