using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DPlayer.Models;
using DPlayer.Services.PlaylistFiles;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// PlaylistFileService 测试（Phase 18）。用临时目录造真实文件：
/// 服务层只判存在性与后缀，不读音频内容，所以空 .mp3 占位文件足够。
/// </summary>
public sealed class PlaylistFileServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly PlaylistFileService _service = new();

    public PlaylistFileServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"DPlayerPlTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>造一个空的音频占位文件，返回绝对路径。</summary>
    private string MakeAudio(string relativePath)
    {
        var full = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(full, Array.Empty<byte>());
        return full;
    }

    /// <summary>造一个只存在于列表里、磁盘上没有的路径。</summary>
    private string MissingAudio(string relativePath) => Path.Combine(_tempDir, relativePath);

    private string WriteList(string fileName, string content, Encoding? encoding = null)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false));
        return path;
    }

    // —— 路径解析与归一化 ——

    [Fact]
    public async Task Import_AbsolutePaths_ReturnedAsIs()
    {
        var a = MakeAudio(@"Music\a.mp3");
        var list = WriteList("abs.m3u", a + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal(new[] { a }, result.AcceptedPaths.ToArray());
        Assert.Equal(1, result.TotalEntries);
        Assert.Equal(0, result.SkippedMissing);
        Assert.Equal(0, result.SkippedUnsupported);
    }

    [Fact]
    public async Task Import_RelativePaths_ResolvedAgainstListDirectory()
    {
        var a = MakeAudio(@"Music\a.mp3");
        Directory.CreateDirectory(Path.Combine(_tempDir, "Lists"));
        var list = Path.Combine(_tempDir, "Lists", "rel.m3u8");
        File.WriteAllText(list, @"..\Music\a.mp3" + "\n", new UTF8Encoding(false));

        var result = await _service.ImportAsync(list);

        Assert.Equal(new[] { a }, result.AcceptedPaths.ToArray());
    }

    [Fact]
    public async Task Import_MixedSeparatorsAndDotDot_Normalized()
    {
        var a = MakeAudio(@"Music\Sub\a.flac");
        var list = WriteList("mix.m3u", @"Music/Sub/../Sub/a.flac" + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Single(result.AcceptedPaths);
        Assert.Equal(Path.GetFullPath(a), result.AcceptedPaths[0]);
    }

    // —— 格式差异 ——

    [Fact]
    public async Task Import_M3u_ExtinfAndCommentsProduceNoEntries()
    {
        var a = MakeAudio(@"a.mp3");
        var list = WriteList("ext.m3u", "#EXTM3U\n#EXTINF:200,Artist - Title\n" + a + "\n#note\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal(1, result.TotalEntries);
        Assert.Equal(new[] { a }, result.AcceptedPaths.ToArray());
    }

    [Fact]
    public async Task Import_Pls_ReadsFileKeysIgnoresRest()
    {
        var a = MakeAudio(@"a.mp3");
        var b = MakeAudio(@"b.wma");
        var list = WriteList("x.pls",
            "[playlist]\nFile1=" + a + "\nTitle1=T\nLength1=1\nFile2=" + b + "\nNumberOfEntries=2\nVersion=2\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal(new[] { a, b }, result.AcceptedPaths.ToArray());
        Assert.Equal(2, result.TotalEntries);
    }

    [Fact]
    public async Task Import_Pls_UrlEntryCountsAsUnsupported()
    {
        var list = WriteList("radio.pls",
            "[playlist]\nFile1=http://stream.example.com/radio\nFile2=https://x.y/z.mp3\n");

        var result = await _service.ImportAsync(list);

        Assert.Empty(result.AcceptedPaths);
        Assert.Equal(2, result.TotalEntries);
        Assert.Equal(2, result.SkippedUnsupported);
        Assert.Equal(0, result.SkippedMissing);
    }

    // —— 过滤计数 ——

    [Fact]
    public async Task Import_MissingFileCountsAsMissing()
    {
        var a = MakeAudio(@"a.mp3");
        var gone = MissingAudio(@"gone.mp3");
        var list = WriteList("m.m3u", a + "\n" + gone + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal(new[] { a }, result.AcceptedPaths.ToArray());
        Assert.Equal(2, result.TotalEntries);
        Assert.Equal(1, result.SkippedMissing);
        Assert.Equal(0, result.SkippedUnsupported);
    }

    [Fact]
    public async Task Import_NonWhitelistedExtensionCountsAsUnsupported()
    {
        var ogg = MakeAudio(@"a.ogg");       // 存在，但后缀不在白名单
        var txt = MissingAudio(@"notes.txt"); // 磁盘上不存在，且后缀不在白名单
        var list = WriteList("u.m3u", ogg + "\n" + txt + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Empty(result.AcceptedPaths);
        Assert.Equal(2, result.SkippedUnsupported);
        // notes.txt 不存在却计入 unsupported 而非 missing：只有后缀判定先于存在性判定才会如此。
        // 若两者顺序被颠倒，它会先撞上 File.Exists 而计入 missing（unsupported=1, missing=1），此断言即失败。
        Assert.Equal(0, result.SkippedMissing);
    }

    [Fact]
    public async Task Import_UppercaseExtensionAccepted()
    {
        var upper = MakeAudio(@"A.MP3");
        var list = WriteList("case.m3u", upper + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Single(result.AcceptedPaths);
    }

    [Fact]
    public async Task Import_DuplicateEntriesAreKept()
    {
        var a = MakeAudio(@"a.mp3");
        var list = WriteList("dup.m3u", a + "\n" + a + "\n" + a + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal(3, result.AcceptedPaths.Count);
    }

    // —— 编码 ——

    [Fact]
    public async Task Import_GbkEncodedChinesePaths_DecodeAndResolve()
    {
        var a = MakeAudio(@"音乐\晴天.mp3");
        var list = WriteList("gbk.m3u", @"音乐\晴天.mp3" + "\n", Encoding.GetEncoding(936));

        // 先证明这个样本确实走的是回退分支：它的字节不是合法 UTF-8
        Assert.ThrowsAny<DecoderFallbackException>(
            () => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(list)));

        var result = await _service.ImportAsync(list);

        Assert.Equal(new[] { a }, result.AcceptedPaths.ToArray());
        Assert.Equal(0, result.SkippedMissing);
    }

    // —— 边界：绝不抛 ——

    [Fact]
    public async Task Import_EmptyOrCommentOnlyFile_ReturnsEmptyResult()
    {
        var list = WriteList("empty.m3u", "#EXTM3U\n\n#nothing\n");

        var result = await _service.ImportAsync(list);

        Assert.Empty(result.AcceptedPaths);
        Assert.Equal(0, result.TotalEntries);
        Assert.Equal("empty", result.SuggestedName);
    }

    [Fact]
    public async Task Import_ListFileDoesNotExist_ReturnsEmptyResultWithoutThrowing()
    {
        var result = await _service.ImportAsync(Path.Combine(_tempDir, "nope.m3u"));

        Assert.Empty(result.AcceptedPaths);
        Assert.Equal(0, result.TotalEntries);
        Assert.Equal("nope", result.SuggestedName);
    }

    [Fact]
    public async Task Import_SuggestedNameComesFromFileNameWithoutExtension()
    {
        var list = WriteList("我的歌单.m3u8", "#EXTM3U\n");

        var result = await _service.ImportAsync(list);

        Assert.Equal("我的歌单", result.SuggestedName);
    }

    // —— 导出 ——

    private static Track MakeTrack(string path, string title, string? artist, TimeSpan duration) =>
        new(path, title, artist, null, null, null, null, null, duration, null);

    [Fact]
    public async Task Export_WritesExtm3uHeaderAndExtinfPairs()
    {
        var dest = Path.Combine(_tempDir, "out.m3u8");
        var tracks = new[]
        {
            MakeTrack(@"C:\Music\a.mp3", "晴天", "周杰伦", TimeSpan.FromSeconds(269.4)),
        };

        await _service.ExportAsync(dest, tracks);

        var bytes = File.ReadAllBytes(dest);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "导出文件不应带 BOM");

        var text = new UTF8Encoding(false).GetString(bytes);
        Assert.Equal("#EXTM3U\r\n#EXTINF:269,周杰伦 - 晴天\r\nC:\\Music\\a.mp3\r\n", text);
    }

    [Fact]
    public async Task Export_MissingArtistWritesTitleOnly()
    {
        var dest = Path.Combine(_tempDir, "noartist.m3u8");

        await _service.ExportAsync(dest, new[] { MakeTrack(@"C:\Music\b.flac", "Instrumental", null, TimeSpan.Zero) });

        var text = File.ReadAllText(dest, new UTF8Encoding(false));
        Assert.Contains("#EXTINF:-1,Instrumental\r\n", text);
        // Ordinal：xUnit 字符串断言默认文化敏感比较；导出格式是逐字节规格，按字面子串判定才准确。
        Assert.DoesNotContain(" - ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_BlankTitleFallsBackToFileName()
    {
        var dest = Path.Combine(_tempDir, "notitle.m3u8");

        await _service.ExportAsync(dest, new[] { MakeTrack(@"C:\Music\c.wav", "  ", "Artist", TimeSpan.FromSeconds(10)) });

        Assert.Contains("#EXTINF:10,Artist - c.wav\r\n", File.ReadAllText(dest, new UTF8Encoding(false)));
    }

    [Fact]
    public async Task Export_PreservesQueueOrder()
    {
        var dest = Path.Combine(_tempDir, "order.m3u8");
        var tracks = new[]
        {
            MakeTrack(@"C:\1.mp3", "One", null, TimeSpan.FromSeconds(1)),
            MakeTrack(@"C:\2.mp3", "Two", null, TimeSpan.FromSeconds(2)),
            MakeTrack(@"C:\3.mp3", "Three", null, TimeSpan.FromSeconds(3)),
        };

        await _service.ExportAsync(dest, tracks);

        var lines = File.ReadAllLines(dest).Where(l => !l.StartsWith('#')).ToArray();
        Assert.Equal(new[] { @"C:\1.mp3", @"C:\2.mp3", @"C:\3.mp3" }, lines);
    }

    [Fact]
    public async Task RoundTrip_ExportThenImport_ReturnsSamePathsInOrder()
    {
        var a = MakeAudio(@"Music\a.mp3");
        var b = MakeAudio(@"Music\b.flac");
        var c = MakeAudio(@"Music\c.wav");
        var dest = Path.Combine(_tempDir, "roundtrip.m3u8");

        await _service.ExportAsync(dest, new[]
        {
            MakeTrack(a, "A", "Artist A", TimeSpan.FromSeconds(100)),
            MakeTrack(b, "B", null, TimeSpan.FromSeconds(200)),
            MakeTrack(c, "C", "Artist C", TimeSpan.Zero),
        });

        var result = await _service.ImportAsync(dest);

        Assert.Equal(new[] { a, b, c }, result.AcceptedPaths.ToArray());
        Assert.Equal(3, result.TotalEntries);
        Assert.Equal(0, result.SkippedMissing);
        Assert.Equal(0, result.SkippedUnsupported);
    }
}
