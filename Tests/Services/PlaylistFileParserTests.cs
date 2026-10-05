using System.Linq;
using DPlayer.Services.PlaylistFiles;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// PlaylistFileFormats / M3uParser / PlsParser 测试（Phase 18）。
/// 解析器只负责"文本 → 原始条目字符串"；路径归一化与过滤在 PlaylistFileService。
/// </summary>
public sealed class PlaylistFileParserTests
{
    // —— PlaylistFileFormats.IsPlaylistFile ——

    [Theory]
    [InlineData("a.m3u", true)]
    [InlineData("a.M3U8", true)]
    [InlineData(@"D:\lists\b.PLS", true)]
    [InlineData("a.mp3", false)]
    [InlineData("a.txt", false)]
    [InlineData("noextension", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPlaylistFile_MatchesOnlyPlaylistExtensions(string? path, bool expected)
    {
        Assert.Equal(expected, PlaylistFileFormats.IsPlaylistFile(path));
    }

    [Fact]
    public void Extensions_AreTheThreeSupportedFormats()
    {
        Assert.Equal(new[] { ".m3u", ".m3u8", ".pls" }, PlaylistFileFormats.Extensions.ToArray());
    }

    // —— M3uParser ——

    [Fact]
    public void M3u_IgnoresCommentsBlankLinesAndExtinf()
    {
        const string text = "#EXTM3U\r\n" +
                            "#EXTINF:200,Artist - Title\r\n" +
                            "\r\n" +
                            "  song1.mp3  \r\n" +
                            "# 用户注释\r\n" +
                            "song2.flac\n";

        Assert.Equal(new[] { "song1.mp3", "song2.flac" }, M3uParser.Parse(text).ToArray());
    }

    [Fact]
    public void M3u_KeepsAbsolutePathsAndDuplicatesAsIs()
    {
        const string text = @"C:\Music\a.mp3" + "\n" + @"C:\Music\a.mp3" + "\n" + @"\\nas\share\b.wma";

        Assert.Equal(3, M3uParser.Parse(text).Count);
        Assert.Equal(@"C:\Music\a.mp3", M3uParser.Parse(text)[0]);
        Assert.Equal(@"\\nas\share\b.wma", M3uParser.Parse(text)[2]);
    }

    [Fact]
    public void M3u_EmptyOrCommentOnly_ReturnsEmpty()
    {
        Assert.Empty(M3uParser.Parse(""));
        Assert.Empty(M3uParser.Parse("#EXTM3U\n#EXTINF:1,x\n"));
    }

    // —— PlsParser ——

    [Fact]
    public void Pls_TakesOnlyFileKeysInAppearanceOrder()
    {
        const string text = "[playlist]\r\n" +
                            "File1=C:\\Music\\a.mp3\r\n" +
                            "Title1=Some Title\r\n" +
                            "Length1=200\r\n" +
                            "File2=C:\\Music\\b.flac\r\n" +
                            "NumberOfEntries=2\r\n" +
                            "Version=2\r\n";

        Assert.Equal(new[] { @"C:\Music\a.mp3", @"C:\Music\b.flac" }, PlsParser.Parse(text).ToArray());
    }

    [Fact]
    public void Pls_KeepsAppearanceOrderWhenNumbersAreOutOfOrder()
    {
        const string text = "[playlist]\nFile3=third.mp3\nFile1=first.mp3\nFile10=tenth.mp3\n";

        Assert.Equal(new[] { "third.mp3", "first.mp3", "tenth.mp3" }, PlsParser.Parse(text).ToArray());
    }

    [Fact]
    public void Pls_KeyMatchingIsCaseInsensitive()
    {
        const string text = "[Playlist]\nfile1=a.mp3\nFILE2=b.mp3\nFiLe3=c.mp3\n";

        Assert.Equal(new[] { "a.mp3", "b.mp3", "c.mp3" }, PlsParser.Parse(text).ToArray());
    }

    [Fact]
    public void Pls_IgnoresSectionsInCommentsAndMalformedLines()
    {
        const string text = "; comment\n" +
                            "# comment\n" +
                            "[playlist]\n" +
                            "no-equals-sign\n" +
                            "=dangling-value\n" +
                            "File=\n" +
                            "FileX=not-a-number.mp3\n" +
                            "File1=real.mp3\n";

        Assert.Equal(new[] { "real.mp3" }, PlsParser.Parse(text).ToArray());
    }
}
