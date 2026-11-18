using System.Buffers.Binary;
using System.Text;
using MiYing.Core.Crypto;
using MiYing.Core.Naming;
using Xunit;

namespace MiYing.Core.Tests;

public class FormatValidationTests
{
    private const string Password = "格式测试-Password1";

    private static async Task<byte[]> EncryptToBytesAsync(byte[] data)
    {
        using var source = new MemoryStream(data);
        using var encrypted = new MemoryStream();
        await VideoCipher.EncryptAsync(source, encrypted, Password, TestHelpers.FastOptions("t.mp4"));
        return encrypted.ToArray();
    }

    [Fact]
    public void NotMsvFile_Throws()
    {
        using var stream = new MemoryStream(TestHelpers.RandomData(200));
        Assert.Throws<MsvNotMsvFileException>(() => MsvHeader.ReadFrom(stream));
    }

    [Fact]
    public async Task TruncatedFile_ThrowsCorrupt()
    {
        byte[] encrypted = await EncryptToBytesAsync(TestHelpers.RandomData(200_000));
        using var stream = new MemoryStream(encrypted[..^16]);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MsvCorruptFileException>(
            () => VideoCipher.DecryptAsync(stream, output, Password));
    }

    [Fact]
    public async Task AppendedTail_ThrowsCorrupt()
    {
        byte[] encrypted = await EncryptToBytesAsync(TestHelpers.RandomData(5000));
        byte[] modified = [.. encrypted, 0xFF];
        using var stream = new MemoryStream(modified);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MsvCorruptFileException>(
            () => VideoCipher.DecryptAsync(stream, output, Password));
    }

    [Fact]
    public async Task TamperedChunkPayload_ThrowsCorrupt()
    {
        byte[] encrypted = await EncryptToBytesAsync(TestHelpers.RandomData(200_000));
        int headerLength = MsvHeader.GetHeaderLength("t.mp4");
        int offsetInSecondChunk = headerLength
            + MsvConstants.MinChunkSize + MsvConstants.GcmTagLength
            + 123;
        encrypted[offsetInSecondChunk] ^= 0xFF;

        using var stream = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MsvCorruptFileException>(
            () => VideoCipher.DecryptAsync(stream, output, Password));
    }

    [Fact]
    public async Task TamperedHeaderIterations_ThrowsInvalidPassword()
    {
        byte[] encrypted = await EncryptToBytesAsync(TestHelpers.RandomData(10_000));
        int iterations = BinaryPrimitives.ReadInt32BigEndian(encrypted.AsSpan(8));
        BinaryPrimitives.WriteInt32BigEndian(encrypted.AsSpan(8), iterations + 1);

        using var stream = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MsvInvalidPasswordException>(
            () => VideoCipher.DecryptAsync(stream, output, Password));
    }

    [Fact]
    public async Task UnsupportedVersion_Throws()
    {
        byte[] encrypted = await EncryptToBytesAsync(TestHelpers.RandomData(1000));
        encrypted[4] = 99;
        using var stream = new MemoryStream(encrypted);
        using var output = new MemoryStream();
        await Assert.ThrowsAsync<MsvUnsupportedFormatException>(
            () => VideoCipher.DecryptAsync(stream, output, Password));
    }

    [Fact]
    public void Header_TruncateUtf8_NeverSplitsMultiByteChars()
    {
        string longName = new string('影', 500) + ".mp4";
        byte[] bytes = MsvHeader.TruncateUtf8(longName, MsvConstants.FileNameMaxBytes);
        Assert.True(bytes.Length <= MsvConstants.FileNameMaxBytes);
        Assert.NotEmpty(bytes);

        string decoded = Encoding.UTF8.GetString(bytes);
        Assert.False(decoded.Contains('\uFFFD'));
    }

    [Fact]
    public void Naming_GeneratesExpectedPaths()
    {
        string encrypted = MsvNaming.GetEncryptedPath(@"C:\videos\旅行.MP4");
        Assert.Equal(@"C:\videos\旅行.msv", encrypted);

        string restored = MsvNaming.GetRestoredPath(@"C:\videos\旅行.msv", "旅行.MP4");
        Assert.Equal(@"C:\videos\旅行.MP4", restored);

        Assert.Equal("a_b.mp4", MsvNaming.SanitizeFileName("a:b.mp4"));
    }

    [Fact]
    public void Naming_MakeUniquePath_AppendsSuffix()
    {
        using var temp = new TestHelpers.TempDirectory();
        string path = temp.File("video.mp4");
        File.WriteAllBytes(path, [1]);
        string unique = MsvNaming.MakeUniquePath(path);
        Assert.Equal(temp.File("video (1).mp4"), unique);
    }
}