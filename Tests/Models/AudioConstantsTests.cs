using DPlayer.Models;
using Xunit;

namespace DPlayer.Tests.Models;

public sealed class AudioConstantsTests
{
    [Theory]
    [InlineData("song.mp3")]
    [InlineData("song.MP3")]
    [InlineData(@"C:\Music\a.flac")]
    [InlineData("track.WaV")]
    public void IsAudioFile_AcceptsWhitelistedExtensions(string path)
        => Assert.True(AudioConstants.IsAudioFile(path));

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("song")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAudioFile_RejectsOthers(string? path)
        => Assert.False(AudioConstants.IsAudioFile(path));

    [Fact]
    public void FileDialogFilter_MatchesWhitelist()
    {
        Assert.Equal(5, AudioConstants.AudioExtensions.Count);
        foreach (var extension in AudioConstants.AudioExtensions)
            Assert.Contains($"*{extension}", AudioConstants.FileDialogFilter);
    }
}
