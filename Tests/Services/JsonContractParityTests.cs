using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DPlayer.Configuration;
using DPlayer.Models;
using DPlayer.Services;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// Phase 29 源生成契约守卫: 同一对象分别用反射元数据和源生成元数据序列化,
/// 输出必须逐字节一致。两边共用同一套写入选项(WriteIndented / JsonStringEnumConverter),
/// 因此差异只可能来自元数据来源本身。
/// </summary>
public sealed class JsonContractParityTests
{
    private static readonly JsonSerializerOptions Reflection = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonSerializerOptions SourceGen = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = DPlayerJsonContext.Default,
    };

    [Fact]
    public void QueueState_SourceGenMatchesReflection()
    {
        var state = new QueueState
        {
            Playlists = new[]
            {
                new Playlist("p1", "P", new[] { @"C:\Music\a.mp3" }, 1,
                    true, RepeatMode.One),
            },
            CurrentPlaylistId = "p1",
            ShuffleEnabled = true,
            RepeatMode = RepeatMode.List,
            LibraryPlayback = new Playlist(
                "__library-playback__", "库", Array.Empty<string>(), -1,
                false, RepeatMode.Off),
        };

        Assert.Equal(
            JsonSerializer.Serialize(state, Reflection),
            JsonSerializer.Serialize(state, SourceGen));
    }

    [Fact]
    public void AppSettings_SourceGenMatchesReflection()
    {
        var settings = new AppSettings
        {
            DefaultVolume = 0.35f,
            LibraryFolders = new[] { @"C:\Music" },
            EqualizerBands = new double[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 },
        };

        Assert.Equal(
            JsonSerializer.Serialize(settings, Reflection),
            JsonSerializer.Serialize(settings, SourceGen));
    }

    [Fact]
    public void LibraryCacheDictionary_SourceGenMatchesReflection()
    {
        var cache = new Dictionary<string, List<LibraryCacheEntry>>
        {
            [@"C:\Music"] = new()
            {
                new LibraryCacheEntry(@"C:\Music\a.mp3", "A", "Artist",
                    "Album", null, 2024, System.TimeSpan.FromMinutes(3),
                    44100, 1),
            },
        };

        Assert.Equal(
            JsonSerializer.Serialize(cache, Reflection),
            JsonSerializer.Serialize(cache, SourceGen));
    }
}
