using MiYing.Core.Crypto;
using MiYing.Core.Naming;
using Xunit;

namespace MiYing.Core.Tests;

public class PhotoNamingTests
{
    [Theory]
    [InlineData("a.jpg")]
    [InlineData("a.JPEG")]
    [InlineData("a.png")]
    [InlineData("a.bmp")]
    [InlineData("a.gif")]
    [InlineData("a.tiff")]
    [InlineData("a.jfif")]
    [InlineData("a.jpe")]
    [InlineData("a.webp")]
    [InlineData("a.heic")]
    [InlineData("a.avif")]
    public void ImageExtensions_AreRecognized(string name)
    {
        Assert.True(MsvNaming.IsKnownImageExtension(name));
        Assert.False(MsvNaming.IsKnownVideoExtension(name));
    }

    [Theory]
    [InlineData("a.mp4")]
    [InlineData("a.MKV")]
    [InlineData("a.mov")]
    public void VideoExtensions_AreNotImages(string name)
    {
        Assert.True(MsvNaming.IsKnownVideoExtension(name));
        Assert.False(MsvNaming.IsKnownImageExtension(name));
    }

    [Theory]
    [InlineData(@"C:\photos\旅行.JPG", @"C:\photos\旅行.msp")]
    [InlineData(@"C:\photos\全家福.png", @"C:\photos\全家福.msp")]
    [InlineData(@"C:\videos\旅行.MP4", @"C:\videos\旅行.msv")]
    public void EncryptedPath_UsesExpectedExtension(string source, string expected)
    {
        Assert.Equal(expected, MsvNaming.GetEncryptedPath(source));
    }

    [Theory]
    [InlineData("photo.jpg", null, MediaKind.Photo)]
    [InlineData("video.mp4", null, MediaKind.Video)]
    [InlineData("", @"C:\x\a.msp", MediaKind.Photo)]
    [InlineData("", @"C:\x\a.msv", MediaKind.Unknown)]
    [InlineData("archive.bin", @"C:\x\a.msp", MediaKind.Photo)]
    [InlineData("archive.bin", @"C:\x\a.msv", MediaKind.Unknown)]
    [InlineData(null, @"C:\x\a.unknown", MediaKind.Unknown)]
    public void DetectMediaKind_Works(string? originalName, string? encryptedPath, MediaKind expected)
    {
        Assert.Equal(expected, MsvNaming.DetectMediaKind(originalName, encryptedPath));
    }

    [Theory]
    [InlineData(@"C:\x\a.msv", true)]
    [InlineData(@"C:\x\a.MSP", true)]
    [InlineData(@"C:\x\a.jpg", false)]
    public void EncryptedFile_Detection(string path, bool expected)
    {
        Assert.Equal(expected, MsvNaming.IsEncryptedFile(path));
    }

    [Fact]
    public void RestoredPath_KeepsPhotoExtension()
    {
        string restored = MsvNaming.GetRestoredPath(@"C:\photos\a.msp", "全家福.JPG");
        Assert.Equal(@"C:\photos\全家福.JPG", restored);
    }

    [Fact]
    public void RestoredPath_WithoutOriginalName_UsesMediaFallback()
    {
        Assert.Equal(@"C:\photos\a.jpg", MsvNaming.GetRestoredPath(@"C:\photos\a.msp", ""));
        Assert.Equal(@"C:\videos\a.mp4", MsvNaming.GetRestoredPath(@"C:\videos\a.msv", ""));
    }

    [Fact]
    public async Task PhotoFile_RoundTrips_AndKeepsOriginalName()
    {
        using var temp = new TestHelpers.TempDirectory();
        string sourcePath = temp.File("photo.jpg");
        byte[] data = TestHelpers.RandomData(150_000);
        await File.WriteAllBytesAsync(sourcePath, data);

        string encryptedPath = temp.File("photo.msp");
        await VideoCipher.EncryptFileAsync(sourcePath, encryptedPath, "Photo!123456", TestHelpers.FastOptions("photo.jpg"));

        MsvHeaderInfo info = MsvVideo.ReadHeader(encryptedPath);
        Assert.Equal("photo.jpg", info.Header.OriginalFileName);
        Assert.Equal(MediaKind.Photo, MsvNaming.DetectMediaKind(info.Header.OriginalFileName, encryptedPath));
        Assert.True(await VideoCipher.VerifyEncryptedFileAsync(encryptedPath, "Photo!123456"));

        string restoredPath = temp.File("restored.jpg");
        await VideoCipher.DecryptFileAsync(encryptedPath, restoredPath, "Photo!123456");
        Assert.Equal(data, await File.ReadAllBytesAsync(restoredPath));
    }
}