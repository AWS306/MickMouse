using MickMouse.Web.Infrastructure;
using Xunit;

namespace MickMouse.Tests;

public sealed class MediaValidationTests
{
    [Theory]
    [InlineData("field.mp3", "audio/mpeg")]
    [InlineData("field.wav", "audio/wav")]
    [InlineData("field.ogg", "audio/ogg")]
    [InlineData("field.m4a", "audio/mp4")]
    [InlineData("field.mp4", "video/mp4")]
    [InlineData("field.webm", "video/webm")]
    public void RecognizedExtensionAndMimePairsAreAccepted(string name, string type)
    {
        Assert.True(MediaValidation.IsAllowed(name, type, 1024));
    }

    [Theory]
    [InlineData("payload.html", "text/html")]
    [InlineData("payload.exe", "audio/mpeg")]
    [InlineData("recording.mp3.exe", "audio/mpeg")]
    [InlineData("recording.mp3", "video/mp4")]
    [InlineData("recording", "audio/mpeg")]
    public void UnsupportedOrMismatchedFilesAreRejected(string name, string type)
    {
        Assert.False(MediaValidation.IsAllowed(name, type, 1024));
    }

    [Fact]
    public void UploadLimitHasAnExplicitBoundary()
    {
        Assert.True(MediaValidation.IsAllowed("field.wav", "audio/wav", MediaValidation.MaxBytes));
        Assert.False(MediaValidation.IsAllowed("field.wav", "audio/wav", MediaValidation.MaxBytes + 1));
        Assert.False(MediaValidation.IsAllowed("field.wav", "audio/wav", 0));
        Assert.False(MediaValidation.IsAllowed("field.wav", "audio/wav", -1));
    }
}
