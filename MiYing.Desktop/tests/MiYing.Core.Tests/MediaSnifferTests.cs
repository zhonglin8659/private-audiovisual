using MiYing.Core.Crypto;
using MiYing.Core.Naming;
using Xunit;

namespace MiYing.Core.Tests;

public class MediaSnifferTests
{
    private static byte[] Header(params byte[] prefix)
    {
        var data = new byte[64];
        prefix.CopyTo(data, 0);
        return data;
    }

    [Fact]
    public void Jpeg_IsPhoto()
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(Header(0xFF, 0xD8, 0xFF, 0xE0)));

    [Fact]
    public void Png_IsPhoto()
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(Header(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)));

    [Fact]
    public void Gif_IsPhoto()
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect("GIF89a"u8.ToArray()));

    [Fact]
    public void Bmp_IsPhoto()
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(Header((byte)'B', (byte)'M')));

    [Theory]
    [InlineData(new byte[] { (byte)'I', (byte)'I', 0x2A, 0x00 })]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0x00, 0x2A })]
    public void Tiff_IsPhoto(byte[] magic)
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(Header(magic)));

    [Fact]
    public void Ico_IsPhoto()
        => Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(Header(0x00, 0x00, 0x01, 0x00)));

    [Fact]
    public void Webp_IsPhoto()
    {
        byte[] data = Header((byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P');
        Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(data));
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("heix")]
    [InlineData("mif1")]
    [InlineData("avif")]
    public void HeifFamily_IsPhoto(string brand)
    {
        byte[] data = Header(0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p');
        brand.Select(c => (byte)c).ToArray().CopyTo(data, 8);
        Assert.Equal(MediaKind.Photo, MediaSniffer.Detect(data));
    }

    [Theory]
    [InlineData("isom")]
    [InlineData("mp42")]
    [InlineData("qt  ")]
    public void IsoBmffVideo_IsVideo(string brand)
    {
        byte[] data = Header(0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p');
        brand.Select(c => (byte)c).ToArray().CopyTo(data, 8);
        Assert.Equal(MediaKind.Video, MediaSniffer.Detect(data));
    }

    [Fact]
    public void RiffWithoutWebp_IsUnknown()
    {
        byte[] data = Header((byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'A', (byte)'V', (byte)'E');
        Assert.Equal(MediaKind.Unknown, MediaSniffer.Detect(data));
    }

    [Fact]
    public void Empty_IsUnknown()
        => Assert.Equal(MediaKind.Unknown, MediaSniffer.Detect(ReadOnlySpan<byte>.Empty));

    [Fact]
    public async Task ContentDetection_FindsPhotoAfterEncryption()
    {
        using var temp = new TestHelpers.TempDirectory();
        string source = temp.File("photo.bin");
        string encrypted = temp.File("photo.msv");
        byte[] data = Header(0xFF, 0xD8, 0xFF, 0xE1);
        await File.WriteAllBytesAsync(source, data);
        await VideoCipher.EncryptFileAsync(source, encrypted, "Sniff!123456", TestHelpers.FastOptions("photo.bin"));

        Assert.Equal(MediaKind.Photo, MsvVideo.DetectMediaKindByContent(encrypted, "Sniff!123456"));
    }

    [Fact]
    public async Task ContentDetection_FindsVideoAfterEncryption()
    {
        using var temp = new TestHelpers.TempDirectory();
        string source = temp.File("video.bin");
        string encrypted = temp.File("video.msv");
        byte[] data = Header(0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m');
        await File.WriteAllBytesAsync(source, data);
        await VideoCipher.EncryptFileAsync(source, encrypted, "Sniff!123456", TestHelpers.FastOptions("video.bin"));

        Assert.Equal(MediaKind.Video, MsvVideo.DetectMediaKindByContent(encrypted, "Sniff!123456"));
    }
}