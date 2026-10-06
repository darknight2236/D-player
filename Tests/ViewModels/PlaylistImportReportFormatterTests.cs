using System;
using DPlayer.ViewModels;
using Xunit;

namespace DPlayer.Tests.ViewModels;

/// <summary>PlaylistImportReportFormatter 文案分支测试（Phase 18）。</summary>
public sealed class PlaylistImportReportFormatterTests
{
    private static PlaylistImportReport Report(
        string source, string? playlist, bool created, int imported, int missing = 0, int unsupported = 0)
        => new(source, playlist, created, imported, missing, unsupported, imported + missing + unsupported);

    [Fact]
    public void Format_SingleNewPlaylist_WithSkips()
    {
        var text = PlaylistImportReportFormatter.Format(
            new[] { Report("rock.m3u8", "rock", true, 18, missing: 1, unsupported: 1) });

        Assert.Equal(
            "已导入「rock.m3u8」→ 新歌单「rock」\n18 首入列，跳过 2 条（文件缺失 1 / 格式不支持 1）",
            text);
    }

    [Fact]
    public void Format_SingleAppend_NoSkips()
    {
        var text = PlaylistImportReportFormatter.Format(
            new[] { Report("rock.m3u8", "我的歌单", false, 18) });

        Assert.Equal("已导入「rock.m3u8」→ 歌单「我的歌单」\n18 首入列", text);
    }

    [Fact]
    public void Format_AllSkipped_SaysNothingImportable()
    {
        var text = PlaylistImportReportFormatter.Format(
            new[] { Report("old.pls", null, false, 0, missing: 3, unsupported: 2) });

        Assert.Equal("「old.pls」没有可导入的条目\n跳过 5 条（文件缺失 3 / 格式不支持 2）", text);
    }

    [Fact]
    public void Format_MultipleReports_BulletedSummary()
    {
        var text = PlaylistImportReportFormatter.Format(new[]
        {
            Report("rock.m3u8", "rock", true, 18, missing: 1, unsupported: 1),
            Report("old.pls", null, false, 0, missing: 3, unsupported: 2),
        });

        Assert.Equal(
            "已导入 2 个播放列表\n" +
            "· rock.m3u8 → 新歌单「rock」：18 首（跳过 2）\n" +
            "· old.pls → 没有可导入的条目（跳过 5）",
            text);
    }

    [Fact]
    public void Format_EmptyOrNull_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, PlaylistImportReportFormatter.Format(Array.Empty<PlaylistImportReport>()));
        Assert.Equal(string.Empty, PlaylistImportReportFormatter.Format(null!));
    }
}
