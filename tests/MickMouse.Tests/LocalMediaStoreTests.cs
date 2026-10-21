using System.Text;
using MickMouse.Web.Infrastructure;
using Xunit;

namespace MickMouse.Tests;

public sealed class LocalMediaStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mick-media-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ADeclaredAudioFileWithHtmlContentsIsRejectedWithoutSavingIt()
    {
        var store = new LocalMediaStore(_directory);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<script>alert('bad upload')</script>"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(stream, "recording.mp3", "audio/mpeg"));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_directory, "uploads")));
    }

    [Fact]
    public async Task UploadUsesAGeneratedKeyAndCanBeReadAndDeleted()
    {
        var store = new LocalMediaStore(_directory);
        byte[] header = [0x49, 0x44, 0x33, 0x04, 0, 0, 0, 0, 0, 0];
        await using var stream = new MemoryStream(header);
        var key = await store.SaveAsync(stream, "my recording.mp3", "audio/mpeg");
        Assert.StartsWith("uploads/", key);
        Assert.DoesNotContain("my recording", key);
        Assert.Equal(header, await File.ReadAllBytesAsync(store.GetLocalPath(key)));
        Assert.Contains(Uri.EscapeDataString(key), await store.GetPlaybackUrlAsync(key));
        await store.DeleteAsync(key);
        Assert.False(File.Exists(store.GetLocalPath(key)));
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("uploads/../../secret")]
    [InlineData("uploads\\file.mp3")]
    [InlineData("/etc/passwd")]
    [InlineData("samples/.hidden")]
    public async Task TraversalAndUntrustedKeysAreRejected(string key)
    {
        var store = new LocalMediaStore(_directory);
        Assert.Throws<ArgumentException>(() => store.GetLocalPath(key));
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetPlaybackUrlAsync(key));
    }

    [Fact]
    public async Task SharedSampleMediaIsRetainedDuringDelete()
    {
        var store = new LocalMediaStore(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "samples"));
        var path = store.GetLocalPath("samples/shared.mp3");
        await File.WriteAllTextAsync(path, "Shared sample content");
        await store.DeleteAsync("samples/shared.mp3");
        Assert.True(File.Exists(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
