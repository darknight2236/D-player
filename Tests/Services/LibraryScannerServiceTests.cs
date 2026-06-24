using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NSubstitute;
using UmaPlayer.Models;
using UmaPlayer.Services;
using Xunit;

namespace UmaPlayer.Tests.Services;

/// <summary>
/// LibraryScannerService 单元测试 (Phase 10)。
/// 使用真实文件系统创建临时目录, IDisposable 清理。
/// </summary>
public class LibraryScannerServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ITrackMetadataReader _reader = Substitute.For<ITrackMetadataReader>();
    private readonly LibraryScannerService _sut;

    public LibraryScannerServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "UmaPlayerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _sut = new LibraryScannerService(_reader);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, true); }
        catch { /* best effort */ }
    }

    private string CreateFile(string relativePath, byte[]? content = null)
    {
        var full = Path.Combine(_tempRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content ?? Array.Empty<byte>());
        return full;
    }

    // -- ScanFolder --

    [Fact]
    public void ScanFolder_ReturnsAudioFiles()
    {
        var mp3 = CreateFile("song.mp3");
        var flac = CreateFile("track.flac");
        CreateFile("readme.txt");

        var result = _sut.ScanFolder(_tempRoot);

        Assert.Contains(mp3, result);
        Assert.Contains(flac, result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ScanFolder_ScansSubdirectories()
    {
        var nested = CreateFile(Path.Combine("sub", "deep", "audio.wav"));

        var result = _sut.ScanFolder(_tempRoot);

        Assert.Single(result);
        Assert.Equal(nested, result[0]);
    }

    [Fact]
    public void ScanFolder_EmptyDirectory_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(_tempRoot, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = _sut.ScanFolder(emptyDir);

        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_NonexistentDirectory_ReturnsEmpty()
    {
        var result = _sut.ScanFolder(Path.Combine(_tempRoot, "no_such_dir"));

        Assert.Empty(result);
    }

    [Fact]
    public void ScanFolder_IsCaseInsensitive()
    {
        CreateFile("UPPER.MP3");
        CreateFile("Mixed.Flac");

        var result = _sut.ScanFolder(_tempRoot);

        Assert.Equal(2, result.Count);
    }

    // -- ComputeDiff --

    [Fact]
    public void ComputeDiff_EmptyCurrentAndCache_ReturnsEmpty()
    {
        var diff = _sut.ComputeDiff(Array.Empty<string>(), Array.Empty<LibraryCacheEntry>());

        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_EmptyCurrentNonEmptyCache_ReturnsAllRemoved()
    {
        var cache = new List<LibraryCacheEntry>
        {
            new("C:\\a.mp3", "A", null, null, null, null, TimeSpan.FromSeconds(30), 44100, null),
            new("C:\\b.mp3", "B", null, null, null, null, TimeSpan.FromSeconds(60), 44100, null),
        };

        var diff = _sut.ComputeDiff(Array.Empty<string>(), cache);

        Assert.Equal(2, diff.RemovedPaths.Count);
        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_NonEmptyCurrentEmptyCache_ReturnsAllAdded()
    {
        var files = new List<string> { "C:\\a.mp3", "C:\\b.mp3" };

        var diff = _sut.ComputeDiff(files, Array.Empty<LibraryCacheEntry>());

        Assert.Equal(2, diff.AddedPaths.Count);
        Assert.Empty(diff.RemovedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_MatchingPaths_ReturnsUnchanged()
    {
        var path = CreateFile("song.mp3");
        var cache = new List<LibraryCacheEntry>
        {
            new(path, "Song", "Artist", "Album", null, null, TimeSpan.FromSeconds(180), 44100, null),
        };

        var diff = _sut.ComputeDiff(new[] { path }, cache);

        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Single(diff.Unchanged);
        Assert.Equal("Song", diff.Unchanged[0].Title);
    }

    [Fact]
    public void ComputeDiff_FileDeletedFromDisk_ReturnsRemoved()
    {
        var path = CreateFile("gone.mp3");
        var cache = new List<LibraryCacheEntry>
        {
            new(path, "Gone", null, null, null, null, TimeSpan.FromSeconds(100), 44100, null),
        };

        // 删除文件
        File.Delete(path);

        var diff = _sut.ComputeDiff(Array.Empty<string>(), cache);

        Assert.Single(diff.RemovedPaths);
        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_CaseInsensitivePathMatching()
    {
        var path = CreateFile("Song.MP3");
        // 缓存中使用不同大小写
        var cache = new List<LibraryCacheEntry>
        {
            new(path.ToLowerInvariant(), "Song", null, null, null, null, TimeSpan.FromSeconds(100), 44100, null),
        };

        var diff = _sut.ComputeDiff(new[] { path }, cache);

        // 大小写不同但应视为同一文件 → unchanged
        Assert.Empty(diff.AddedPaths);
        Assert.Empty(diff.RemovedPaths);
        Assert.Single(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_CachedWithZeroDuration_TreatedAsAdded()
    {
        var path = CreateFile("bad.mp3");
        var cache = new List<LibraryCacheEntry>
        {
            new(path, "Bad", null, null, null, null, TimeSpan.Zero, null, null), // Duration=0, SampleRate=null
        };

        var diff = _sut.ComputeDiff(new[] { path }, cache);

        // 零时长 + 无采样率 → 视为需要重新读取
        Assert.Single(diff.AddedPaths);
        Assert.Empty(diff.Unchanged);
    }

    [Fact]
    public void ComputeDiff_CachedWithZeroDurationAndZeroSampleRate_TreatedAsAdded()
    {
        var path = CreateFile("bad2.mp3");
        var cache = new List<LibraryCacheEntry>
        {
            new(path, "Bad2", null, null, null, null, TimeSpan.Zero, 0, null),
        };

        var diff = _sut.ComputeDiff(new[] { path }, cache);

        Assert.Single(diff.AddedPaths);
        Assert.Empty(diff.Unchanged);
    }

    // -- ReadMetadataBatchAsync --

    [Fact]
    public async Task ReadMetadataBatchAsync_EmptyPaths_ReturnsEmpty()
    {
        var result = await _sut.ReadMetadataBatchAsync(Array.Empty<string>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_NullPaths_ReturnsEmpty()
    {
        var result = await _sut.ReadMetadataBatchAsync(null!);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_ReadsAllPaths()
    {
        var paths = new[] { "a.mp3", "b.mp3", "c.mp3" };
        var track = new Track("a.mp3", "T", null, null, null, null, null, null, TimeSpan.Zero, null);
        _reader.ReadAsync(Arg.Any<string>()).Returns(track);

        var result = await _sut.ReadMetadataBatchAsync(paths);

        Assert.Equal(3, result.Count);
        await _reader.Received(3).ReadAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task ReadMetadataBatchAsync_SkipsFailedReads()
    {
        var paths = new[] { "good.mp3", "bad.mp3" };
        var goodTrack = new Track("good.mp3", "Good", null, null, null, null, null, null, TimeSpan.Zero, null);

        _reader.ReadAsync("good.mp3").Returns(goodTrack);
        _reader.ReadAsync("bad.mp3").Returns(Task.FromException<Track>(new IOException("corrupt")));

        var result = await _sut.ReadMetadataBatchAsync(paths);

        Assert.Single(result);
        Assert.Equal("Good", result[0].Title);
    }
}
