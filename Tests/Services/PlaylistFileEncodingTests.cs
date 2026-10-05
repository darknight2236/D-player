using System;
using System.Text;
using DPlayer.Services.PlaylistFiles;
using Xunit;

namespace DPlayer.Tests.Services;

/// <summary>
/// PlaylistFileEncoding 编码探测测试（Phase 18）。
/// 覆盖：UTF-8 无 BOM / UTF-8 BOM / UTF-16LE BOM / UTF-16BE BOM / GBK 回退 / 空输入。
/// </summary>
public sealed class PlaylistFileEncodingTests
{
    public PlaylistFileEncodingTests()
    {
        // 测试进程不走 App.OnStartup，需要自己注册才能用 936 造 GBK 字节。
        // RegisterProvider 幂等，与被测类型的静态构造函数重复注册无冲突。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private const string Chinese = @"D:\音乐\周杰伦\晴天.mp3";

    [Fact]
    public void Decode_Utf8NoBom_ChinesePathSurvives()
    {
        var bytes = new UTF8Encoding(false).GetBytes(Chinese);

        Assert.Equal(Chinese, PlaylistFileEncoding.Decode(bytes));
    }

    [Fact]
    public void Decode_Utf8WithBom_BomNotLeakedIntoText()
    {
        // GetBytes 不含 preamble，手动拼 BOM 才能走到探测分支
        var body = new UTF8Encoding(false).GetBytes(Chinese);
        var withBom = new byte[3 + body.Length];
        new byte[] { 0xEF, 0xBB, 0xBF }.CopyTo(withBom, 0);
        body.CopyTo(withBom, 3);

        var text = PlaylistFileEncoding.Decode(withBom);

        Assert.Equal(Chinese, text);
        // DoesNotContain(string, string) 默认按 CurrentCulture 比较，而 U+FEFF 在 ICU 排序里
        // 是可忽略字符，会在任意字符串 pos 0 "命中"；必须显式 Ordinal 才是在查真实码位。
        Assert.DoesNotContain("\uFEFF", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_Utf16LeBom_Decodes()
    {
        var body = Encoding.Unicode.GetBytes(Chinese);
        var withBom = new byte[2 + body.Length];
        new byte[] { 0xFF, 0xFE }.CopyTo(withBom, 0);
        body.CopyTo(withBom, 2);

        Assert.Equal(Chinese, PlaylistFileEncoding.Decode(withBom));
    }

    [Fact]
    public void Decode_Utf16BeBom_Decodes()
    {
        var body = Encoding.BigEndianUnicode.GetBytes(Chinese);
        var withBom = new byte[2 + body.Length];
        new byte[] { 0xFE, 0xFF }.CopyTo(withBom, 0);
        body.CopyTo(withBom, 2);

        Assert.Equal(Chinese, PlaylistFileEncoding.Decode(withBom));
    }

    [Fact]
    public void Decode_GbkBytes_FallsBackTo936()
    {
        var gbk = Encoding.GetEncoding(936);
        var bytes = gbk.GetBytes(Chinese);

        // 关键断言：这些字节不是合法 UTF-8（否则测不到回退分支）
        Assert.ThrowsAny<DecoderFallbackException>(
            () => new UTF8Encoding(false, true).GetString(bytes));

        Assert.Equal(Chinese, PlaylistFileEncoding.Decode(bytes));
    }

    [Fact]
    public void Decode_EmptyBytes_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, PlaylistFileEncoding.Decode(Array.Empty<byte>()));
    }
}
