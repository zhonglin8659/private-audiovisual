using MiYing.Core.Crypto;
using Xunit;

namespace MiYing.Core.Tests;

public class EncryptedStreamTests
{
    private const string Password = "stream-测试密码-123";

    private static async Task<(byte[] Data, string Path, TestHelpers.TempDirectory Temp)> CreateEncryptedFileAsync(int size)
    {
        var temp = new TestHelpers.TempDirectory();
        byte[] data = TestHelpers.RandomData(size);
        string sourcePath = temp.File("src.bin");
        await File.WriteAllBytesAsync(sourcePath, data);
        string encryptedPath = temp.File("src.msv");
        await VideoCipher.EncryptFileAsync(sourcePath, encryptedPath, Password, TestHelpers.FastOptions());
        return (data, encryptedPath, temp);
    }

    [Fact]
    public async Task SequentialRead_MatchesOriginal()
    {
        (byte[] data, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(300_000);
        using (temp)
        using (var stream = MsvVideo.OpenPlaybackStream(path, Password))
        {
            Assert.Equal(data.Length, (int)stream.Length);
            using var output = new MemoryStream();
            await stream.CopyToAsync(output);
            Assert.Equal(data, output.ToArray());
        }
    }

    [Fact]
    public async Task RandomSeekReads_MatchOriginal()
    {
        (byte[] data, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(300_000);
        using (temp)
        using (var stream = MsvVideo.OpenPlaybackStream(path, Password))
        {
            var random = new Random(20261016);
            var buffer = new byte[8192];
            for (int i = 0; i < 200; i++)
            {
                long position = random.NextInt64(0, data.Length - buffer.Length);
                stream.Position = position;
                int read = stream.Read(buffer, 0, buffer.Length);
                Assert.Equal(buffer.Length, read);
                Assert.True(buffer.AsSpan(0, read).SequenceEqual(data.AsSpan((int)position, read)));
            }
        }
    }

    [Fact]
    public async Task SeekEndAndBeyondEnd_BehaveCorrectly()
    {
        (byte[] data, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(70_000);
        using (temp)
        using (var stream = MsvVideo.OpenPlaybackStream(path, Password))
        {
            Assert.Equal(data.Length, (int)stream.Seek(0, SeekOrigin.End));
            Assert.Equal(0, stream.Read(new byte[16], 0, 16));

            stream.Seek(-10, SeekOrigin.End);
            var tail = new byte[10];
            Assert.Equal(10, stream.Read(tail, 0, 10));
            Assert.Equal(data[(data.Length - 10)..], tail);

            Assert.Throws<IOException>(() => stream.Seek(-5, SeekOrigin.Begin));
        }
    }

    [Fact]
    public async Task LargeReadAcrossChunks_MatchesOriginal()
    {
        (byte[] data, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(300_000);
        using (temp)
        using (var stream = MsvVideo.OpenPlaybackStream(path, Password))
        {
            stream.Position = MsvConstants.MinChunkSize - 100;
            var buffer = new byte[200_000];
            int read = stream.Read(buffer, 0, buffer.Length);
            Assert.Equal(buffer.Length, read);
            Assert.Equal(
                data[(MsvConstants.MinChunkSize - 100)..(MsvConstants.MinChunkSize - 100 + read)],
                buffer);
        }
    }

    [Fact]
    public async Task WrongPassword_ThrowsOnOpen()
    {
        (byte[] _, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(100_000);
        using (temp)
        {
            Assert.Throws<MsvInvalidPasswordException>(() => MsvVideo.OpenPlaybackStream(path, "bad-password"));
        }
    }

    [Fact]
    public async Task VerifyPassword_ReflectsCorrectness()
    {
        (byte[] _, string path, TestHelpers.TempDirectory temp) = await CreateEncryptedFileAsync(100_000);
        using (temp)
        {
            Assert.True(MsvVideo.VerifyPassword(path, Password));
            Assert.False(MsvVideo.VerifyPassword(path, "bad-password"));
        }
    }
}