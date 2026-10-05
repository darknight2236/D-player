# Phase 18 播放列表文件导入导出（M3U / M3U8 / PLS）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让歌单能与外部世界互通——导入 `.m3u` / `.m3u8` / `.pls`（新建歌单或追加当前歌单），导出当前歌单为 `.m3u8`（绝对路径 + 完整 `#EXTINF`），导入侧严格过滤不可用条目并弹出主题化结果报告。

**Architecture:** 新增 `Services/PlaylistFiles/` 门面模块：`IPlaylistFileService` 编排「读字节 → 编码探测 → 格式解析 → 路径归一化 → 存在性/白名单过滤计数」，读侧绝不抛、写侧异常冒到 VM。两个 VM 各暴露一个可 await 的公开方法（容器级 = 新建歌单，歌单级 = 追加 + 导出），返回结构化 `PlaylistImportReport`；中文文案由 View 层 `PlaylistImportReportFormatter` 组装、经 `ConfirmDialog.ShowInfo` 弹出。拖拽与对话框共用同一管道（VM 方法的 `presetPath` 参数就是接点）。

**Tech Stack:** .NET 10（`net10.0-windows`）· WPF · CommunityToolkit.Mvvm · `System.Text.Encoding.CodePages`（GBK 回退）· xUnit + NSubstitute · 复用 `Models/AudioConstants` 白名单与 `Themes/Icons.xaml` 矢量图标

**设计稿：** [`docs/superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md`](../specs/2026-10-05-d-player-phase18-playlist-file-io-design.md)

**验证基线：** 每个 Task 结束后 `dotnet build D-player.sln -c Debug --nologo -v q` → **0 错误 0 警告**；`dotnet test D-player.sln -c Debug --nologo -v q` → 原有 **96 个测试无回归** + 本阶段新增测试全绿。最后一个 Task 做 ComputerUse GUI 实地验收。

## Global Constraints

- 目标框架 `net10.0-windows`；新包版本策略与现有 `Microsoft.Extensions.*` 一致用浮动 `10.*`。
- 音频后缀白名单**只有一处来源**：`Models/AudioConstants.AudioExtensions` = `.mp3 .wma .flac .aac .wav`。任何过滤都引用它，不得复制字面量。
- 播放列表文件后缀：`.m3u` `.m3u8` `.pls`，唯一定义在 `PlaylistFileFormats.Extensions`。
- 读侧（`ImportAsync`）**绝不抛异常**；写侧（`ExportAsync`）**必须抛**，由 VM 捕获转文案。不要把 `ExportAsync` 包成不抛。
- 导出一律 UTF-8 **无 BOM** + `\r\n` 行尾；导入按 BOM → 严格 UTF-8 试解码 → GBK(936) 回退。
- `#EXTINF` / PLS 的 `Title=` / `Length=` / `NumberOfEntries` / `Version` **一律忽略**：标题与时长只信 ATL 从音频文件读到的结果。
- 导入建出的歌单 `SourceFolder` **必须为 null**（普通歌单，不写 library cache、不显示"刷新文件夹"按钮）。
- 网络流条目（`http://` `https://` `mms://` `rtsp://`）计入 `SkippedUnsupported`，本阶段不做流播放。
- 列表内重复条目**不去重**，原样保留顺序与重复。
- 分层纪律：VM **不拼中文展示文案**（只返回结构化 record）；View **不做路径解析或过滤**（只调服务/VM）。
- 所有 `Queue` 修改必须在 UI 线程：VM 里的 `await` 用 `.ConfigureAwait(true)`，不要用 `ConfigureAwait(false)`。
- 拖拽 Drop / Click 事件处理器是 `async void`：**必须** try/catch 兜住异常，否则未观察异常会崩进程。
- 提交信息风格：`type(scope): subject` + 要点式 body（见 `git log`）；每个 Task 至少一次提交。
- 仓库 `core.autocrlf=true`：新建文本文件写完后用 `unix2dos <file>` 转 CRLF 再 `git add`；**禁止**用 `sed -i` 就地改文件（会剥掉 CRLF）。

---

## 文件结构

| 文件 | 责任 | Task |
|------|------|------|
| `D-player.csproj`（**最终未改动**） | 原计划加 `System.Text.Encoding.CodePages` 包引用；该包在 net10.0 框架隐含（显式引用触发 NU1510），无需改动（见 Task 1 Step 1 执行记录） | 1 |
| `Services/PlaylistFiles/PlaylistFileEncoding.cs`（新） | 字节 → 文本：BOM 判定 / 严格 UTF-8 试解码 / GBK 回退；静态构造函数注册 CodePages provider | 1 |
| `Services/PlaylistFiles/PlaylistFileFormats.cs`（新） | 后缀白名单 + `IsPlaylistFile` + 对话框过滤器字符串（public，供 View 拖拽判定） | 2 |
| `Services/PlaylistFiles/M3uParser.cs`（新） | M3U/M3U8 文本 → 原始条目行 | 2 |
| `Services/PlaylistFiles/PlsParser.cs`（新） | PLS(INI) 文本 → 原始条目行（只取 `File<N>=`） | 2 |
| `Services/PlaylistFiles/PlaylistImportResult.cs`（新） | 服务层导入结果 record | 3 |
| `Services/PlaylistFiles/IPlaylistFileService.cs`（新） | 门面接口：`ImportAsync` / `ExportAsync` | 3 |
| `Services/PlaylistFiles/PlaylistFileService.cs`（新） | 编排：读文件 → 解码 → 选解析器 → 归一化 → 过滤计数；导出委托 `M3u8Writer` | 3、4 |
| `Extensions/ServiceCollectionExtensions.cs`（改） | 注册 `IPlaylistFileService`；更新 `PlaylistViewModel` 工厂 lambda | 3、6 |
| `Services/PlaylistFiles/M3u8Writer.cs`（新） | `Track` 列表 → extended M3U8 文本 | 4 |
| `Services/IFileDialogService.cs`（改） | 加 `SaveFile(filter, defaultFileName, defaultExtension)` | 5 |
| `Services/Win32FileDialogService.cs`（改） | 用 `Microsoft.Win32.SaveFileDialog` 实现 `SaveFile` | 5 |
| `ViewModels/PlaylistViewModel.cs`（改） | ctor 加 `IPlaylistFileService`；`ImportPlaylistFileAsync` / `ExportPlaylistFileAsync` | 6 |
| `ViewModels/PlaylistImportReport.cs`（新） | VM 层结构化报告 record（View 拼文案用） | 6 |
| `ViewModels/PlaylistsViewModel.cs`（改） | ctor 加依赖 + `NullPlaylistFileService` + `NullFileDialogService.SaveFile`；容器级 `ImportPlaylistFileAsync`（新建歌单） | 5、7 |
| `Views/Dialogs/ConfirmDialog.xaml.cs`（改） | 加 `ShowInfo`（单按钮，语义为信息而非错误） | 8 |
| `Views/Controls/DragDropExtensions.cs`（改） | 加 `PlaylistFileExtensions` + `FilterPlaylistPaths` | 8 |
| `Themes/Icons.xaml`（改） | 加 `Icon.Import` / `Icon.Export` | 8 |
| `Views/Controls/PlaylistImportReportFormatter.cs`（新） | 报告 record → 中文文案（单个/多个/全跳过分支） | 8 |
| `Views/Controls/PlaylistImportUi.cs`（新） | View 层共用执行器：跑导入 → 聚合报告 → 弹框 → 兜异常 | 8 |
| `Views/Controls/PlaylistsSidebarView.xaml(.cs)`（改） | 第三个图标按钮（导入 → 新建歌单）；`DragOver`/`Drop` 加 FileDrop 分支 | 9 |
| `Views/Controls/PlaylistView.xaml(.cs)`（改） | 工具栏加"导入列表"/"导出列表"；拖拽分流音频与播放列表文件 | 10 |
| `docs/PROJECT.md`、`docs/COUPLING.md`、`README.md`（改） | Phase 18 小节、§5 隐式契约 7 条、§7 don't-do 2 条、功能列表 | 11 |
| `Tests/Services/PlaylistFileEncodingTests.cs`（新） | 编码探测 6 例 | 1 |
| `Tests/Services/PlaylistFileParserTests.cs`（新） | 后缀判定 + M3U + PLS 解析 9 例 | 2 |
| `Tests/Services/PlaylistFileServiceTests.cs`（新） | 导入 12 例（Task 3）+ 导出/往返 5 例（Task 4） | 3、4 |
| `Tests/ViewModels/PlaylistViewModelTests.cs`（改） | 构造签名修正 + 导入/导出 8 例 | 6 |
| `Tests/ViewModels/PlaylistsViewModelTests.cs`（改） | 构造签名修正 + 新建歌单导入 5 例 | 7 |
| `Tests/Views/PlaylistImportReportFormatterTests.cs`（新） | 文案分支 5 例 | 8 |

---

### Task 1: CodePages 依赖 + 编码探测层

**Files:**
- Modify: `D-player.csproj`
- Create: `Services/PlaylistFiles/PlaylistFileEncoding.cs`
- Test: `Tests/Services/PlaylistFileEncodingTests.cs`

**Interfaces:**
- Consumes: 无（本阶段起点）
- Produces: `internal static class DPlayer.Services.PlaylistFiles.PlaylistFileEncoding`，成员 `static string Decode(byte[] bytes)`。

> **与设计稿 §8 的偏离（有意）**：设计稿写"在 `App.OnStartup` 最早期注册 `CodePagesEncodingProvider`"。实现改为在 `PlaylistFileEncoding` 的**静态构造函数**里注册——静态构造函数保证在该类型任何成员被调用前执行，而 `Encoding.GetEncoding(936)` 只在这个类里出现，于是"注册必须早于解码"从一条需要人记住的启动顺序契约，变成类型自身保证的不变量，`App.xaml.cs` 也不用改。Task 11 会把设计稿 §8 与 COUPLING 条目改成这个口径。

- [x] **Step 1: 加包引用**

> **执行记录（实现期修订，Task 11 回写）**：本步骤最终**未执行**——`System.Text.Encoding.CodePages` 在 net10.0 上是**框架隐含**（framework-implicit）的，`CodePagesEncodingProvider` / `Encoding.GetEncoding(936)` 开箱可用；显式 `PackageReference` 会被 SDK 判定冗余并触发 **NU1510** 警告，破坏本项目 0 警告门禁。因此 `D-player.csproj` **未改动**，没有新增任何包引用（见 commit `27ed09c` 与设计稿决策记录 #8）。

原计划（已作废）：在 `D-player.csproj` 的 `<ItemGroup>`（含其它 `PackageReference` 的那组）末尾，`z440.atl.core` 之后追加一行：

```xml
        <PackageReference Include="System.Text.Encoding.CodePages" Version="10.*" />
```

- [x] **Step 2: 验证还原成功**

> **执行记录**：随 Step 1 作废——未加包引用，无需验证还原。

原计划（已作废）：Run: `dotnet restore D-player.sln --nologo -v q`
Expected: 无错误。若报 `10.*` 无法解析（该包版本节奏偶有滞后），改为 `Version="9.*"` 再 restore 一次，并在提交信息里记一句实际采用的版本。

- [x] **Step 3: 写失败测试**

创建 `Tests/Services/PlaylistFileEncodingTests.cs`：

```csharp
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
        Assert.DoesNotContain("\uFEFF", text);
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
```

- [x] **Step 4: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileEncodingTests"`
Expected: 编译失败 —— `DPlayer.Services.PlaylistFiles` 命名空间/类型不存在（CS0246）。

- [x] **Step 5: 实现 `PlaylistFileEncoding`**

创建 `Services/PlaylistFiles/PlaylistFileEncoding.cs`：

```csharp
using System;
using System.Text;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件的字节 → 文本解码（Phase 18）。
///
/// 探测顺序：BOM（UTF-8 / UTF-16LE / UTF-16BE）→ 无 BOM 时用"非法字节即抛"的严格 UTF-8
/// 试解码 → 失败回退 GBK(936)。传统 .m3u 在中文 Windows 上多为 ANSI/GBK，
/// 只按 UTF-8 读会让整表中文路径变成 U+FFFD，进而被"文件缺失"过滤掉。
///
/// GBK 依赖 CodePagesEncodingProvider：在静态构造函数里注册，保证早于本类任何解码调用，
/// 不依赖 App 启动顺序（RegisterProvider 幂等）。
/// </summary>
internal static class PlaylistFileEncoding
{
    private static readonly UTF8Encoding StrictUtf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlaylistFileEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string Decode(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(936).GetString(bytes);
        }
    }
}
```

- [x] **Step 6: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileEncodingTests"`
Expected: 6 通过 / 0 失败。

再跑全量确认无回归：`dotnet test D-player.sln -c Debug --nologo -v q` → 102 通过（96 + 6）。

- [x] **Step 7: 转 CRLF 并提交**

```bash
unix2dos Services/PlaylistFiles/PlaylistFileEncoding.cs Tests/Services/PlaylistFileEncodingTests.cs
git add D-player.csproj Services/PlaylistFiles/PlaylistFileEncoding.cs Tests/Services/PlaylistFileEncodingTests.cs
git commit -m "feat(services): playlist file encoding probe with GBK fallback (Phase 18)

- Add System.Text.Encoding.CodePages so Encoding.GetEncoding(936) works
- PlaylistFileEncoding.Decode: BOM (UTF-8/UTF-16LE/BE) -> strict UTF-8
  probe -> GBK fallback; provider registered in the static ctor so the
  registration can never lag behind a decode call"
```

---

### Task 2: 格式常量 + M3U / PLS 解析器

**Files:**
- Create: `Services/PlaylistFiles/PlaylistFileFormats.cs`
- Create: `Services/PlaylistFiles/M3uParser.cs`
- Create: `Services/PlaylistFiles/PlsParser.cs`
- Test: `Tests/Services/PlaylistFileParserTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public static class DPlayer.Services.PlaylistFiles.PlaylistFileFormats`：`static IReadOnlyList<string> Extensions`、`const string OpenFilter`、`const string SaveFilter`、`static bool IsPlaylistFile(string? path)`
  - `internal static class M3uParser`：`static IReadOnlyList<string> Parse(string text)`
  - `internal static class PlsParser`：`static IReadOnlyList<string> Parse(string text)`

- [x] **Step 1: 写失败测试**

创建 `Tests/Services/PlaylistFileParserTests.cs`：

```csharp
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
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileParserTests"`
Expected: 编译失败 —— `PlaylistFileFormats` / `M3uParser` / `PlsParser` 不存在（CS0246）。

- [x] **Step 3: 实现 `PlaylistFileFormats`**

创建 `Services/PlaylistFiles/PlaylistFileFormats.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件格式常量与后缀判定（Phase 18）。
///
/// IsPlaylistFile 是静态而非实例方法：View 层拖拽判定（DragDropExtensions）
/// 不该为了一个后缀判断去 DI 取服务。
/// 后缀集合是本主题的唯一来源，DragDropExtensions.PlaylistFileExtensions 代理到这里。
/// </summary>
public static class PlaylistFileFormats
{
    /// <summary>支持的播放列表后缀（小写，含点）。</summary>
    public static readonly IReadOnlyList<string> Extensions = new[] { ".m3u", ".m3u8", ".pls" };

    /// <summary>导入用文件对话框过滤器。</summary>
    public const string OpenFilter = "播放列表|*.m3u;*.m3u8;*.pls|所有文件|*.*";

    /// <summary>导出用保存对话框过滤器。</summary>
    public const string SaveFilter = "M3U8 播放列表|*.m3u8";

    public static bool IsPlaylistFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;

        foreach (var allowed in Extensions)
        {
            if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
```

- [x] **Step 4: 实现两个解析器**

创建 `Services/PlaylistFiles/M3uParser.cs`：

```csharp
using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// M3U / M3U8 解析（Phase 18）：逐行取"非 # 开头的非空行"。
///
/// #EXTM3U / #EXTINF / 用户注释一律忽略 —— 设计稿 §5.1：标题与时长只信
/// ATL 从音频文件读到的结果，不用列表文件里的字符串覆盖。
/// </summary>
internal static class M3uParser
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var entries = new List<string>();
        if (string.IsNullOrEmpty(text)) return entries;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;
            entries.Add(line);
        }
        return entries;
    }
}
```

创建 `Services/PlaylistFiles/PlsParser.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// PLS（INI 风格）解析（Phase 18）：只取 [playlist] 段内 File&lt;N&gt;= 的值。
///
/// 按**出现顺序**返回而非按序号排序 —— 现实中不少工具序号不连续或乱序。
/// Title&lt;N&gt; / Length&lt;N&gt; / NumberOfEntries / Version 一律忽略（同 M3uParser 的理由）。
/// </summary>
internal static class PlsParser
{
    public static IReadOnlyList<string> Parse(string text)
    {
        var entries = new List<string>();
        if (string.IsNullOrEmpty(text)) return entries;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[') continue;                     // 段头 [playlist]
            if (line[0] == '#' || line[0] == ';') continue;   // INI 注释

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;                            // 无 '=' 或以 '=' 开头

            var key = line.Substring(0, eq).Trim();
            if (!IsFileKey(key)) continue;

            var value = line.Substring(eq + 1).Trim();
            if (value.Length == 0) continue;

            entries.Add(value);
        }
        return entries;
    }

    /// <summary>key 形如 File + 至少一位数字（大小写不敏感）。</summary>
    private static bool IsFileKey(string key)
    {
        if (key.Length < 5) return false;                     // 最短合法 key 是 "File1"
        if (!key.StartsWith("File", StringComparison.OrdinalIgnoreCase)) return false;

        for (int i = 4; i < key.Length; i++)
        {
            if (!char.IsDigit(key[i])) return false;
        }
        return true;
    }
}
```

- [x] **Step 5: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileParserTests"`
Expected: 16 通过 / 0 失败（`IsPlaylistFile` 的 Theory 展开 8 例 + 8 个 Fact）。

全量：`dotnet test D-player.sln -c Debug --nologo -v q` → 96 + 6（Task 1）+ 16（本 Task）= 118 通过，全绿。

- [x] **Step 6: 转 CRLF 并提交**

```bash
unix2dos Services/PlaylistFiles/PlaylistFileFormats.cs Services/PlaylistFiles/M3uParser.cs Services/PlaylistFiles/PlsParser.cs Tests/Services/PlaylistFileParserTests.cs
git add Services/PlaylistFiles/PlaylistFileFormats.cs Services/PlaylistFiles/M3uParser.cs Services/PlaylistFiles/PlsParser.cs Tests/Services/PlaylistFileParserTests.cs
git commit -m "feat(services): playlist formats + M3U/PLS parsers (Phase 18)

- PlaylistFileFormats: extension whitelist, dialog filters, static
  IsPlaylistFile so View drop targets need no DI
- M3uParser: non-# non-empty lines only; #EXTINF deliberately ignored
- PlsParser: File<N>= in appearance order; Title/Length/NumberOfEntries
  ignored, sections and INI comments skipped"
```

---

### Task 3: `IPlaylistFileService.ImportAsync`（归一化 + 过滤计数）+ DI 注册

**Files:**
- Create: `Services/PlaylistFiles/PlaylistImportResult.cs`
- Create: `Services/PlaylistFiles/IPlaylistFileService.cs`
- Create: `Services/PlaylistFiles/PlaylistFileService.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `Tests/Services/PlaylistFileServiceTests.cs`

**Interfaces:**
- Consumes: `PlaylistFileEncoding.Decode(byte[])`、`M3uParser.Parse(string)`、`PlsParser.Parse(string)`、`PlaylistFileFormats.Extensions`、`Models.AudioConstants.AudioExtensions`
- Produces:
  - `public sealed record DPlayer.Services.PlaylistFiles.PlaylistImportResult(string SuggestedName, IReadOnlyList<string> AcceptedPaths, int TotalEntries, int SkippedMissing, int SkippedUnsupported)`
  - `public interface IPlaylistFileService { Task<PlaylistImportResult> ImportAsync(string playlistFilePath); }`（`ExportAsync` 在 Task 4 追加，本 Task 不留占位实现）
  - `public sealed class PlaylistFileService : IPlaylistFileService`

- [x] **Step 1: 写失败测试**

创建 `Tests/Services/PlaylistFileServiceTests.cs`：

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        var txt = MakeAudio(@"notes.txt");
        var list = WriteList("u.m3u", ogg + "\n" + txt + "\n");

        var result = await _service.ImportAsync(list);

        Assert.Empty(result.AcceptedPaths);
        Assert.Equal(2, result.SkippedUnsupported);
        Assert.Equal(0, result.SkippedMissing);   // 后缀判定先于存在性判定
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
}
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileServiceTests"`
Expected: 编译失败 —— `PlaylistFileService` / `PlaylistImportResult` 不存在（CS0246）。

- [x] **Step 3: 实现结果 record 与接口**

创建 `Services/PlaylistFiles/PlaylistImportResult.cs`：

```csharp
using System.Collections.Generic;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 一次导入的结构化结果（Phase 18）。
///
/// AcceptedPaths 已完成：相对路径归一化为绝对路径 + 后缀白名单过滤 + File.Exists 校验。
/// 三个计数互斥（一个条目只进一个桶），供 View 层拼"跳过 N 条"文案。
/// </summary>
/// <param name="SuggestedName">建议歌单名 = 列表文件名去后缀；无法取名时为 "导入的歌单"。</param>
/// <param name="AcceptedPaths">可用条目的绝对路径（保留原顺序与重复）。</param>
/// <param name="TotalEntries">列表文件里解析出的原始条目数。</param>
/// <param name="SkippedMissing">路径解析成功但文件不存在。</param>
/// <param name="SkippedUnsupported">网络流条目、后缀不在 AudioConstants 白名单、或路径本身非法。</param>
public sealed record PlaylistImportResult(
    string SuggestedName,
    IReadOnlyList<string> AcceptedPaths,
    int TotalEntries,
    int SkippedMissing,
    int SkippedUnsupported);
```

创建 `Services/PlaylistFiles/IPlaylistFileService.cs`：

```csharp
using System.Threading.Tasks;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// 播放列表文件读写门面（Phase 18）。
///
/// 错误策略刻意不对称（对齐项目既有约定）：
///   ImportAsync 绝不抛 —— 读侧对齐 JsonPlaylistService.LoadAsync，任何失败都退化为空结果，
///   由 View 的"没有可导入的条目"报告兜住，用户仍有反馈而不是崩溃。
/// </summary>
public interface IPlaylistFileService
{
    /// <summary>解析 .m3u / .m3u8 / .pls，返回归一化并过滤后的条目与跳过计数。绝不抛。</summary>
    Task<PlaylistImportResult> ImportAsync(string playlistFilePath);
}
```

> `ExportAsync` 在 Task 4 与 `M3u8Writer` 一起追加到本接口——**不要**在本 Task 先塞一个 `throw new NotImplementedException` 占位，那会把半成品带进提交历史。

- [x] **Step 4: 实现 `PlaylistFileService`（本 Task 只做导入侧）**

创建 `Services/PlaylistFiles/PlaylistFileService.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DPlayer.Models;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// IPlaylistFileService 默认实现（Phase 18）：编排 编码探测 → 解析 → 归一化 → 过滤计数。
/// 无状态，注册为 Singleton。
/// </summary>
public sealed class PlaylistFileService : IPlaylistFileService
{
    private static readonly string[] UrlPrefixes = { "http://", "https://", "mms://", "rtsp://" };

    public async Task<PlaylistImportResult> ImportAsync(string playlistFilePath)
    {
        var suggested = SuggestName(playlistFilePath);

        try
        {
            if (string.IsNullOrWhiteSpace(playlistFilePath) || !File.Exists(playlistFilePath))
                return Empty(suggested);

            var bytes = await File.ReadAllBytesAsync(playlistFilePath).ConfigureAwait(false);
            var text = PlaylistFileEncoding.Decode(bytes);

            var raw = Path.GetExtension(playlistFilePath).Equals(".pls", StringComparison.OrdinalIgnoreCase)
                ? PlsParser.Parse(text)
                : M3uParser.Parse(text);

            var baseDir = Path.GetDirectoryName(Path.GetFullPath(playlistFilePath)) ?? string.Empty;
            return Classify(raw, baseDir, suggested);
        }
        // 读侧绝不抛（设计稿 §4）：文件被占用/无权限/路径含非法字符都退化为空结果，
        // 由 View 的"没有可导入的条目"报告兜住，用户仍能看到反馈而不是崩溃。
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                            or NotSupportedException or ArgumentException)
        {
            return Empty(suggested);
        }
    }

    private static PlaylistImportResult Classify(IReadOnlyList<string> rawEntries, string baseDir, string suggested)
    {
        var accepted = new List<string>(rawEntries.Count);
        int missing = 0, unsupported = 0;

        foreach (var entry in rawEntries)
        {
            if (IsUrl(entry)) { unsupported++; continue; }

            string full;
            try
            {
                full = Path.IsPathRooted(entry)
                    ? Path.GetFullPath(entry)
                    : Path.GetFullPath(Path.Combine(baseDir, entry));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                unsupported++; continue;      // 非法字符 / 无效路径形态
            }

            var ext = Path.GetExtension(full);
            if (!AudioConstants.AudioExtensions.Any(a => string.Equals(a, ext, StringComparison.OrdinalIgnoreCase)))
            {
                unsupported++; continue;      // 后缀判定先于存在性判定
            }

            if (!File.Exists(full)) { missing++; continue; }

            accepted.Add(full);
        }

        return new PlaylistImportResult(suggested, accepted, rawEntries.Count, missing, unsupported);
    }

    private static bool IsUrl(string entry)
    {
        foreach (var prefix in UrlPrefixes)
        {
            if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string SuggestName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "导入的歌单";
        var name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? "导入的歌单" : name;
    }

    private static PlaylistImportResult Empty(string suggested) =>
        new(suggested, Array.Empty<string>(), 0, 0, 0);
}
```

> 本 Task 结束时 `IPlaylistFileService` 只有 `ImportAsync` 一个成员，`PlaylistFileService` 完整实现它——不留任何占位实现。`ExportAsync` 的接口声明与实现一起在 Task 4 追加。

- [x] **Step 5: 注册到 DI**

修改 `Extensions/ServiceCollectionExtensions.cs`：在文件顶部 using 区加

```csharp
using DPlayer.Services.PlaylistFiles;
```

在 `services.AddSingleton<ILibraryCache, JsonLibraryCache>();` 之后加

```csharp

        // Phase 18: 播放列表文件导入导出（无状态、纯文件 IO → Singleton）
        services.AddSingleton<IPlaylistFileService, PlaylistFileService>();
```

- [x] **Step 6: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileServiceTests"`
Expected: 14 通过 / 0 失败。

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

- [x] **Step 7: 转 CRLF 并提交**

```bash
unix2dos Services/PlaylistFiles/PlaylistImportResult.cs Services/PlaylistFiles/IPlaylistFileService.cs Services/PlaylistFiles/PlaylistFileService.cs Tests/Services/PlaylistFileServiceTests.cs
git add Services/PlaylistFiles/PlaylistImportResult.cs Services/PlaylistFiles/IPlaylistFileService.cs Services/PlaylistFiles/PlaylistFileService.cs Extensions/ServiceCollectionExtensions.cs Tests/Services/PlaylistFileServiceTests.cs
git commit -m "feat(services): IPlaylistFileService import pipeline (Phase 18)

- PlaylistFileService.ImportAsync: decode -> parse by extension ->
  resolve relative paths against the list directory -> filter
- Skip buckets are mutually exclusive: URL/extension/illegal-path ->
  SkippedUnsupported, non-existent -> SkippedMissing
- Import never throws (mirrors JsonPlaylistService.LoadAsync); registered
  as a stateless singleton"
```

---

### Task 4: `M3u8Writer` + `ExportAsync` + 往返

**Files:**
- Create: `Services/PlaylistFiles/M3u8Writer.cs`
- Modify: `Services/PlaylistFiles/IPlaylistFileService.cs`（追加 `ExportAsync` 声明与写侧错误策略注释）
- Modify: `Services/PlaylistFiles/PlaylistFileService.cs`（实现 `ExportAsync`）
- Test: `Tests/Services/PlaylistFileServiceTests.cs`（追加导出与往返用例）

**Interfaces:**
- Consumes: `Models.Track`（`FilePath` `Title` `Artist` `Duration`）、`IPlaylistFileService`（Task 3）
- Produces: `internal static class M3u8Writer`，成员 `static readonly Encoding Encoding`（UTF-8 无 BOM）、`static string Write(IReadOnlyList<Track> tracks)`；`IPlaylistFileService.ExportAsync(string destPath, IReadOnlyList<Track> tracks) → Task`（写侧可抛，Task 6 的导出路径依赖它）。

- [x] **Step 1: 追加失败测试**

在 `Tests/Services/PlaylistFileServiceTests.cs` 类内末尾追加（文件顶部 using 已含 `System.Linq` / `System.Text` / `DPlayer.Models` 需要补一行）：

```csharp
using DPlayer.Models;
```

```csharp
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
        Assert.DoesNotContain(" - ", text);
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
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileServiceTests.Export"`
Expected: 编译失败 —— `PlaylistFileService` 上没有 `ExportAsync`（CS1061）。

- [x] **Step 3: 实现 `M3u8Writer`**

创建 `Services/PlaylistFiles/M3u8Writer.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DPlayer.Models;

namespace DPlayer.Services.PlaylistFiles;

/// <summary>
/// extended M3U8 写出（Phase 18）：UTF-8 无 BOM、CRLF 行尾、绝对路径。
///
/// 格式：
///   #EXTM3U
///   #EXTINF:{秒},{Artist - Title}
///   {绝对路径}
/// 秒 = 四舍五入的 Duration.TotalSeconds；未知（&lt;= 0）写 -1。
/// #EXTINF 的显示名里出现逗号无需转义——该字段只按第一个逗号切分。
/// </summary>
internal static class M3u8Writer
{
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string Write(IReadOnlyList<Track> tracks)
    {
        var sb = new StringBuilder();
        sb.Append("#EXTM3U\r\n");

        if (tracks is null) return sb.ToString();

        foreach (var track in tracks)
        {
            var seconds = track.Duration > TimeSpan.Zero
                ? (int)Math.Round(track.Duration.TotalSeconds)
                : -1;

            sb.Append("#EXTINF:").Append(seconds).Append(',').Append(DisplayName(track)).Append("\r\n");
            sb.Append(track.FilePath).Append("\r\n");
        }

        return sb.ToString();
    }

    private static string DisplayName(Track track)
    {
        var title = string.IsNullOrWhiteSpace(track.Title)
            ? Path.GetFileName(track.FilePath)
            : track.Title;

        return string.IsNullOrWhiteSpace(track.Artist)
            ? title
            : track.Artist + " - " + title;
    }
}
```

- [x] **Step 4: 接口加 `ExportAsync` 声明**

`Services/PlaylistFiles/IPlaylistFileService.cs`：using 区补 `using System.Collections.Generic;` 与 `using DPlayer.Models;`，类注释追加写侧策略一行，接口体追加成员：

```csharp
///   ExportAsync 可抛 IOException / UnauthorizedAccessException —— 写侧对齐设置/EQ 对话框的
///   try/catch + 错误框，由 VM 捕获转成错误文案交给 View。
```

```csharp
    /// <summary>把曲目写出为 extended M3U8（UTF-8 无 BOM，CRLF，绝对路径）。失败抛 IOException。</summary>
    Task ExportAsync(string destPath, IReadOnlyList<Track> tracks);
```

- [x] **Step 5: 实现 `PlaylistFileService.ExportAsync`**

在 `Services/PlaylistFiles/PlaylistFileService.cs` 的 `ImportAsync` 之后追加：

```csharp
    public async Task ExportAsync(string destPath, IReadOnlyList<Track> tracks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destPath);
        ArgumentNullException.ThrowIfNull(tracks);

        var text = M3u8Writer.Write(tracks);

        // 写侧必须让异常冒到 VM（设计稿 §4）：VM 捕获后转成错误文案，
        // View 用 ConfirmDialog.ShowError 弹出。不要在这里 try/catch 吞掉。
        await File.WriteAllTextAsync(destPath, text, M3u8Writer.Encoding).ConfigureAwait(false);
    }
```

- [x] **Step 6: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistFileServiceTests"`
Expected: 19 通过 / 0 失败（14 导入 + 5 导出/往返）。

全量：`dotnet test D-player.sln -c Debug --nologo -v q` → 96 + 6 + 16 + 14 + 5 = 137 通过，全绿。

- [x] **Step 7: 转 CRLF 并提交**

```bash
unix2dos Services/PlaylistFiles/M3u8Writer.cs
git add Services/PlaylistFiles/M3u8Writer.cs Services/PlaylistFiles/IPlaylistFileService.cs Services/PlaylistFiles/PlaylistFileService.cs Tests/Services/PlaylistFileServiceTests.cs
git commit -m "feat(services): M3U8 export + import/export round trip (Phase 18)

- M3u8Writer: #EXTM3U header, #EXTINF:{seconds},{Artist - Title} pairs,
  UTF-8 without BOM, CRLF, absolute paths; unknown duration -> -1,
  missing artist -> title only, blank title -> file name
- ExportAsync lets IOException escape so the VM can turn it into a
  themed error dialog"
```

---

### Task 5: 保存对话框（`IFileDialogService.SaveFile`）

**Files:**
- Modify: `Services/IFileDialogService.cs`
- Modify: `Services/Win32FileDialogService.cs`
- Modify: `ViewModels/PlaylistsViewModel.cs`（内部 `NullFileDialogService` 桩）

**Interfaces:**
- Consumes: 无
- Produces: `IFileDialogService.SaveFile(string filter, string defaultFileName, string defaultExtension) → string?`（取消返回 `null`）。Task 6 的导出路径依赖它。

- [x] **Step 1: 接口加方法**

在 `Services/IFileDialogService.cs` 的 `OpenFolder()` 之后追加：

```csharp

    /// <summary>
    /// 弹出保存文件对话框(Phase 18)。覆盖已有文件由系统 OverwritePrompt 询问。
    /// </summary>
    /// <param name="filter">WPF 格式过滤器，如 "M3U8 播放列表|*.m3u8"。</param>
    /// <param name="defaultFileName">默认文件名（调用方负责清洗非法字符）。</param>
    /// <param name="defaultExtension">未输入后缀时自动补上的扩展名，如 ".m3u8"。</param>
    /// <returns>用户选定的目标绝对路径；取消则返回 null。</returns>
    string? SaveFile(string filter, string defaultFileName, string defaultExtension);
```

- [x] **Step 2: Win32 实现**

在 `Services/Win32FileDialogService.cs` 的 `OpenFolder()` 之后追加：

```csharp

    public string? SaveFile(string filter, string defaultFileName, string defaultExtension)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出播放列表",
            Filter = filter,
            FileName = defaultFileName,
            DefaultExt = defaultExtension,
            AddExtension = true,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileName
            : null;
    }
```

`SaveFileDialog` 与已有的 `OpenFileDialog` 同在 `Microsoft.Win32`，文件顶部已有该 using。

- [x] **Step 3: 补测试桩（否则接口实现不全，编译失败）**

在 `ViewModels/PlaylistsViewModel.cs` 末尾的 `private sealed class NullFileDialogService` 里补一行：

```csharp
        public string? SaveFile(string filter, string defaultFileName, string defaultExtension) => null;
```

- [x] **Step 4: 构建 + 全量测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。若报"未实现接口成员"，说明还有别的 `IFileDialogService` 实现类没补——用 `grep -rn ": IFileDialogService" --include=*.cs .` 找全。

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: 全绿（对话框本身无法单测，本 Task 只验证接线不破坏现有测试）。

- [x] **Step 5: 提交**

```bash
git add Services/IFileDialogService.cs Services/Win32FileDialogService.cs ViewModels/PlaylistsViewModel.cs
git commit -m "feat(services): SaveFile on IFileDialogService for playlist export (Phase 18)

- Win32 SaveFileDialog with AddExtension + OverwritePrompt; cancel -> null
- NullFileDialogService stub returns null so the internal test ctor keeps compiling"
```

---

### Task 6: `PlaylistViewModel` 追加导入 + 导出

**Files:**
- Create: `ViewModels/PlaylistImportReport.cs`
- Modify: `ViewModels/PlaylistViewModel.cs`（ctor + 两个方法 + `SanitizeFileName`）
- Modify: `Extensions/ServiceCollectionExtensions.cs`（工厂 lambda）
- Test: `Tests/ViewModels/PlaylistViewModelTests.cs`

**Interfaces:**
- Consumes: `IPlaylistFileService`（Task 3/4）、`IFileDialogService.SaveFile`（Task 5）、`PlaylistFileFormats.OpenFilter` / `.SaveFilter`（Task 2）、既有私有方法 `PlaylistViewModel.DropExternalFiles(IReadOnlyList<string>)`
- Produces:
  - `public sealed record DPlayer.ViewModels.PlaylistImportReport(string SourceFile, string? PlaylistName, bool CreatedNewPlaylist, int Imported, int SkippedMissing, int SkippedUnsupported, int TotalEntries)`，附 `int Skipped`、`bool AnyImported`
  - `public Task<PlaylistImportReport?> PlaylistViewModel.ImportPlaylistFileAsync(string? presetPath = null)`
  - `public Task<string?> PlaylistViewModel.ExportPlaylistFileAsync()`
  - `PlaylistViewModel` ctor 新签名：`(Models.Playlist seed, IPlaybackService player, IFileDialogService fileDialog, ITrackMetadataReader metadataReader, IPlaylistFileService playlistFiles)`

- [x] **Step 1: 修测试构造 + 写失败测试**

`Tests/ViewModels/PlaylistViewModelTests.cs`：在字段区（`_metadataReader` 那几行旁）加

```csharp
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();
```

顶部 using 区加

```csharp
using DPlayer.Services.PlaylistFiles;
```

把第 38 行的构造改为

```csharp
        return new PlaylistViewModel(seed, _player, _fileDialog, _metadataReader, _playlistFiles);
```

然后在类内末尾追加测试。现有 helper 签名是 `private PlaylistViewModel CreateVm(string id = "test-id", string name = "Test")`，直接用 `CreateVm()` / `CreateVm(name: …)`，**不要**新建第二套 helper。注意该文件顶部注释已说明：ctor 会用 `File.Exists` 过滤 `seed.Items`，所以所有用例都从空队列开始、需要曲目时手动 `vm.Queue.Add(...)`。

```csharp
    // —— Phase 18: 播放列表文件导入/导出 ——

    private static PlaylistImportResult ImportResult(
        string name, string[] accepted, int missing = 0, int unsupported = 0)
        => new(name, accepted, accepted.Length + missing + unsupported, missing, unsupported);

    [Fact]
    public async Task ImportPlaylistFileAsync_UserCancelsDialog_ReturnsNull()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(Array.Empty<string>());
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.Null(report);
        await _playlistFiles.DidNotReceive().ImportAsync(Arg.Any<string>());
        Assert.Empty(vm.Queue);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AppendsAcceptedPathsInOrder()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\rock.m3u8" });
        _playlistFiles.ImportAsync(@"D:\lists\rock.m3u8")
            .Returns(ImportResult("rock", new[] { @"D:\m\a.mp3", @"D:\m\b.flac" }));
        _metadataReader.ReadAsync(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), System.IO.Path.GetFileName(ci.ArgAt<string>(0)),
                null, null, null, null, null, null, TimeSpan.Zero, null));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.Equal(2, report!.Imported);
        Assert.False(report.CreatedNewPlaylist);
        Assert.Equal("Test", report.PlaylistName);
        Assert.Equal("rock.m3u8", report.SourceFile);
        Assert.Equal(2, vm.Queue.Count);
        Assert.Equal(@"D:\m\a.mp3", vm.Queue[0].FilePath);
        Assert.Equal(@"D:\m\b.flac", vm.Queue[1].FilePath);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AllEntriesSkipped_LeavesQueueUntouched()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\old.pls" });
        _playlistFiles.ImportAsync(@"D:\lists\old.pls")
            .Returns(ImportResult("old", Array.Empty<string>(), missing: 3, unsupported: 2));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.Equal(0, report!.Imported);
        Assert.Null(report.PlaylistName);
        Assert.Equal(3, report.SkippedMissing);
        Assert.Equal(2, report.SkippedUnsupported);
        Assert.Equal(5, report.Skipped);
        Assert.Empty(vm.Queue);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_PresetPath_SkipsFileDialog()
    {
        _playlistFiles.ImportAsync(@"D:\drop\x.m3u")
            .Returns(ImportResult("x", new[] { @"D:\m\a.mp3" }));
        _metadataReader.ReadAsync(Arg.Any<string>())
            .Returns(ci => new Track(ci.ArgAt<string>(0), "a", null, null, null, null, null, null, TimeSpan.Zero, null));
        var vm = CreateVm();

        var report = await vm.ImportPlaylistFileAsync(@"D:\drop\x.m3u");

        Assert.Equal(1, report!.Imported);
        _fileDialog.DidNotReceive().OpenFiles(Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_EmptyQueue_ReturnsNullWithoutDialog()
    {
        var vm = CreateVm();

        var error = await vm.ExportPlaylistFileAsync();

        Assert.Null(error);
        _fileDialog.DidNotReceive().SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_SanitizesPlaylistNameForDefaultFileName()
    {
        var vm = CreateVm(name: @"My/List:1*");
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        await vm.ExportPlaylistFileAsync();

        _fileDialog.Received(1).SaveFile(PlaylistFileFormats.SaveFilter, "My_List_1_", ".m3u8");
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_UserCancels_DoesNotCallService()
    {
        var vm = CreateVm();
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);

        var error = await vm.ExportPlaylistFileAsync();

        Assert.Null(error);
        await _playlistFiles.DidNotReceive().ExportAsync(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IReadOnlyList<Track>>());
    }

    [Fact]
    public async Task ExportPlaylistFileAsync_ServiceThrows_ReturnsErrorText()
    {
        var vm = CreateVm();
        vm.Queue.Add(new Track(@"D:\m\a.mp3", "A", null, null, null, null, null, null, TimeSpan.Zero, null));
        _fileDialog.SaveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(@"D:\out\a.m3u8");
        _playlistFiles.ExportAsync(@"D:\out\a.m3u8", Arg.Any<System.Collections.Generic.IReadOnlyList<Track>>())
            .Returns(Task.FromException(new IOException("被占用")));

        var error = await vm.ExportPlaylistFileAsync();

        Assert.NotNull(error);
        Assert.Contains("被占用", error);
        Assert.StartsWith("导出失败：", error);
    }
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistViewModelTests"`
Expected: 编译失败 —— ctor 参数不匹配（CS1503）、`ImportPlaylistFileAsync` / `ExportPlaylistFileAsync` / `PlaylistImportReport` 不存在（CS1061/CS0246）。

- [x] **Step 3: 创建报告 record**

创建 `ViewModels/PlaylistImportReport.cs`：

```csharp
namespace DPlayer.ViewModels;

/// <summary>
/// 一次播放列表导入的结构化报告（Phase 18）。
///
/// 分层纪律：VM 只返回数据，中文文案由 View 层 PlaylistImportReportFormatter 组装。
/// 不要在 VM 里拼展示字符串——两个入口（侧边栏新建 / 工具栏追加）与拖拽聚合报告
/// 共用同一份文案规则，放 View 层才能 DRY。
/// </summary>
/// <param name="SourceFile">列表文件名（含后缀），用于文案里的「来源」。</param>
/// <param name="PlaylistName">新建时=新歌单名；追加时=目标歌单名；一条都没导入时=null。</param>
/// <param name="CreatedNewPlaylist">true=容器级新建歌单；false=追加到既有歌单。</param>
/// <param name="Imported">真正入列的曲目数（元数据读取失败的不计）。</param>
public sealed record PlaylistImportReport(
    string SourceFile,
    string? PlaylistName,
    bool CreatedNewPlaylist,
    int Imported,
    int SkippedMissing,
    int SkippedUnsupported,
    int TotalEntries)
{
    public int Skipped => SkippedMissing + SkippedUnsupported;

    public bool AnyImported => Imported > 0;
}
```

- [x] **Step 4: `PlaylistViewModel` ctor 接线**

在 `ViewModels/PlaylistViewModel.cs` 顶部 using 区加

```csharp
using DPlayer.Services.PlaylistFiles;
```

字段区（`_metadataReader` 之后）加

```csharp
    private readonly IPlaylistFileService _playlistFiles;
```

ctor 改为（新增最后一个参数与赋值）：

```csharp
    public PlaylistViewModel(
        Models.Playlist seed,
        IPlaybackService player,
        IFileDialogService fileDialog,
        ITrackMetadataReader metadataReader,
        IPlaylistFileService playlistFiles)
    {
        ArgumentNullException.ThrowIfNull(seed);
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _playlistFiles = playlistFiles ?? throw new ArgumentNullException(nameof(playlistFiles));
```

ctor 其余部分不动。

同步改 `Extensions/ServiceCollectionExtensions.cs` 的工厂 lambda：

```csharp
        services.AddTransient<Func<Models.Playlist, PlaylistViewModel>>(sp => seed =>
            new PlaylistViewModel(
                seed,
                sp.GetRequiredService<IPlaybackService>(),
                sp.GetRequiredService<IFileDialogService>(),
                sp.GetRequiredService<ITrackMetadataReader>(),
                sp.GetRequiredService<IPlaylistFileService>()));
```

- [x] **Step 5: 实现两个方法**

在 `ViewModels/PlaylistViewModel.cs` 的 `DropExternalFiles` 之后追加：

```csharp
    // —— Phase 18: 播放列表文件导入/导出（歌单级 = 追加当前歌单） ——

    /// <summary>
    /// 导入 .m3u/.m3u8/.pls 并**追加**到本歌单（与 AddToQueue 同层级语义）。
    ///
    /// presetPath 非空 = 拖拽入口（跳过文件对话框）；null = 走对话框。
    /// 返回 null 表示用户取消，View 不应弹任何框；返回报告则由 View 拼文案弹信息框。
    /// 过滤已在服务层完成，这里复用 DropExternalFiles（其契约"传入 paths 已过滤"因此
    /// 多了一个调用方 —— COUPLING §5）。
    /// </summary>
    public async Task<PlaylistImportReport?> ImportPlaylistFileAsync(string? presetPath = null)
    {
        var path = presetPath ?? _fileDialog.OpenFiles(PlaylistFileFormats.OpenFilter).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return null;

        var sourceFile = Path.GetFileName(path);
        // ConfigureAwait(true)：Queue 必须在 UI 线程改（WPF CollectionView 要求）
        var result = await _playlistFiles.ImportAsync(path).ConfigureAwait(true);

        if (result.AcceptedPaths.Count == 0)
        {
            return new PlaylistImportReport(sourceFile, null, false, 0,
                result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
        }

        await DropExternalFiles(result.AcceptedPaths).ConfigureAwait(true);

        return new PlaylistImportReport(sourceFile, Name, false, result.AcceptedPaths.Count,
            result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
    }

    /// <summary>
    /// 导出本歌单为 .m3u8（绝对路径 + 完整 #EXTINF）。
    /// 返回 null = 成功或用户取消（两者都不需要 UI 反馈）；非 null = 可直接展示的错误文案。
    /// </summary>
    public async Task<string?> ExportPlaylistFileAsync()
    {
        if (Queue.Count == 0) return null;   // View 层已拦一次，这里兜底

        var dest = _fileDialog.SaveFile(
            PlaylistFileFormats.SaveFilter, SanitizeFileName(Name), ".m3u8");
        if (string.IsNullOrWhiteSpace(dest)) return null;

        try
        {
            await _playlistFiles.ExportAsync(dest, Queue.ToArray()).ConfigureAwait(true);
            return null;
        }
        catch (Exception ex)
        {
            return $"导出失败：{ex.Message}";
        }
    }

    /// <summary>歌单名 → 合法文件名（非法字符替换为 '_'）。空名退化为 "playlist"。</summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "playlist";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            chars[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        }
        return new string(chars);
    }
```

- [x] **Step 6: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistViewModelTests"`
Expected: 全通过（原有 + 新增 8 例）。

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。此时 `PlaylistsViewModelTests.cs:35` 还在用 4 参构造 —— 若报 CS1503，说明该文件也要跟着改：把 `_playlistFiles` 桩加进 `Tests/ViewModels/PlaylistsViewModelTests.cs` 并在其 `CreatePlaylistVm` 里补第 5 个实参（`Substitute.For<IPlaylistFileService>()`），这是 Task 7 的前置修复，一并做掉以保持编译绿色。

- [x] **Step 7: 转 CRLF 并提交**

```bash
unix2dos ViewModels/PlaylistImportReport.cs
git add ViewModels/PlaylistImportReport.cs ViewModels/PlaylistViewModel.cs Extensions/ServiceCollectionExtensions.cs Tests/ViewModels/PlaylistViewModelTests.cs Tests/ViewModels/PlaylistsViewModelTests.cs
git commit -m "feat(vm): playlist-level import (append) and M3U8 export (Phase 18)

- PlaylistViewModel.ImportPlaylistFileAsync: presetPath lets drag & drop
  and the file dialog share one pipeline; accepted paths go through the
  existing DropExternalFiles so no new metadata dependency is needed
- ExportPlaylistFileAsync returns displayable error text instead of
  throwing; cancel and success both return null
- PlaylistImportReport carries counts only - Chinese copy stays in the View
- SanitizeFileName maps invalid chars to '_' for the default file name"
```

---

### Task 7: `PlaylistsViewModel` 容器级导入（新建歌单）

**Files:**
- Modify: `ViewModels/PlaylistsViewModel.cs`（ctor、`NullPlaylistFileService`、`internal` ctor、新方法）
- Test: `Tests/ViewModels/PlaylistsViewModelTests.cs`

**Interfaces:**
- Consumes: `IPlaylistFileService`、`ILibraryScannerService.ReadMetadataBatchAsync`、既有 `HookPlaylistVm` / `_factory` / `Playlist` record、`PlaylistImportReport`（Task 6）
- Produces: `public Task<PlaylistImportReport?> PlaylistsViewModel.ImportPlaylistFileAsync(string? presetPath = null)`；`PlaylistsViewModel` ctor 新签名（末尾追加 `IPlaylistFileService playlistFiles`）。

- [x] **Step 1: 写失败测试**

`Tests/ViewModels/PlaylistsViewModelTests.cs`：字段区补

```csharp
    private readonly ILibraryScannerService _scanner = Substitute.For<ILibraryScannerService>();
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly IPlaylistFileService _playlistFiles = Substitute.For<IPlaylistFileService>();
```

using 区补 `using DPlayer.Services.PlaylistFiles;` 与 `using System.Collections.Generic;`、`using System.Linq;`（缺哪个补哪个）。

**保留**现有 `CreateContainerVm()`（走 `internal` ctor + Null 桩，96 个既有测试依赖它），另加一个用真依赖的 helper：

```csharp
    /// <summary>Phase 18 导入用例专用：走公共 ctor，scanner / playlistFiles 可 mock。</summary>
    private PlaylistsViewModel CreateContainerVmWithMocks()
    {
        return new PlaylistsViewModel(
            seed => CreatePlaylistVm(seed.Id, seed.Name),
            _player, _fileDialog, _scanner, _cache, _metadataReader, _playlistFiles);
    }

    private static PlaylistImportResult ImportResult(
        string name, string[] accepted, int missing = 0, int unsupported = 0)
        => new(name, accepted, accepted.Length + missing + unsupported, missing, unsupported);

    private static Track FallbackTrack(string path) =>
        new(path, System.IO.Path.GetFileName(path), null, null, null, null, null, null, TimeSpan.Zero, null);
```

用例：

```csharp
    // —— Phase 18: 播放列表文件导入（容器级 = 新建歌单） ——

    [Fact]
    public async Task ImportPlaylistFileAsync_CreatesNewPlaylistAndViewIt()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\rock.m3u8" });
        _playlistFiles.ImportAsync(@"D:\lists\rock.m3u8")
            .Returns(ImportResult("rock", new[] { @"D:\m\a.mp3", @"D:\m\b.flac" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new[] { FallbackTrack(@"D:\m\a.mp3"), FallbackTrack(@"D:\m\b.flac") });
        var container = CreateContainerVmWithMocks();
        int stateChanged = 0;
        container.StateChanged += (_, _) => stateChanged++;

        var report = await container.ImportPlaylistFileAsync();

        Assert.NotNull(report);
        Assert.True(report!.CreatedNewPlaylist);
        Assert.Equal(2, report.Imported);
        Assert.Equal("rock", report.PlaylistName);
        Assert.Single(container.Playlists);
        Assert.Equal("rock", container.Playlists[0].Name);
        Assert.Same(container.Playlists[0], container.ViewedPlaylist);
        Assert.Null(container.Playlists[0].SourceFolder);      // 普通歌单，不走 library cache
        Assert.Equal(2, container.Playlists[0].Queue.Count);
        Assert.True(stateChanged > 0);                          // 触发 debounce 存盘
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_AllEntriesSkipped_DoesNotCreatePlaylist()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\old.pls" });
        _playlistFiles.ImportAsync(@"D:\lists\old.pls")
            .Returns(ImportResult("old", Array.Empty<string>(), missing: 3, unsupported: 2));
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Equal(0, report!.Imported);
        Assert.Null(report.PlaylistName);
        Assert.False(report.CreatedNewPlaylist);
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_NoTracksFromMetadataBatch_DoesNotCreatePlaylist()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(new[] { @"D:\lists\x.m3u" });
        _playlistFiles.ImportAsync(@"D:\lists\x.m3u").Returns(ImportResult("x", new[] { @"D:\m\a.mp3" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(Array.Empty<Track>());
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Equal(0, report!.Imported);
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_UserCancels_ReturnsNull()
    {
        _fileDialog.OpenFiles(Arg.Any<string>(), Arg.Any<bool>()).Returns(Array.Empty<string>());
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync();

        Assert.Null(report);
        await _playlistFiles.DidNotReceive().ImportAsync(Arg.Any<string>());
        Assert.Empty(container.Playlists);
    }

    [Fact]
    public async Task ImportPlaylistFileAsync_PresetPath_SkipsFileDialog()
    {
        _playlistFiles.ImportAsync(@"D:\drop\y.m3u8").Returns(ImportResult("y", new[] { @"D:\m\a.mp3" }));
        _scanner.ReadMetadataBatchAsync(Arg.Any<IReadOnlyList<string>>())
            .Returns(new[] { FallbackTrack(@"D:\m\a.mp3") });
        var container = CreateContainerVmWithMocks();

        var report = await container.ImportPlaylistFileAsync(@"D:\drop\y.m3u8");

        Assert.Equal(1, report!.Imported);
        Assert.Equal("y", container.Playlists[0].Name);
        _fileDialog.DidNotReceive().OpenFiles(Arg.Any<string>(), Arg.Any<bool>());
    }
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistsViewModelTests"`
Expected: 编译失败 —— `PlaylistsViewModel` 没有 7 参 ctor（CS1503）、没有 `ImportPlaylistFileAsync`（CS1061）。

- [x] **Step 3: ctor 接线**

`ViewModels/PlaylistsViewModel.cs`：using 区加 `using DPlayer.Services.PlaylistFiles;`；字段区（`_metadataReader` 之后）加

```csharp
    private readonly IPlaylistFileService _playlistFiles;
```

公共 ctor 追加参数与赋值（保持既有顺序，只在末尾加）：

```csharp
    public PlaylistsViewModel(
        Func<Playlist, PlaylistViewModel> factory,
        IPlaybackService player,
        IFileDialogService fileDialog,
        ILibraryScannerService scanner,
        ILibraryCache cache,
        ITrackMetadataReader metadataReader,
        IPlaylistFileService playlistFiles)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metadataReader = metadataReader ?? throw new ArgumentNullException(nameof(metadataReader));
        _playlistFiles = playlistFiles ?? throw new ArgumentNullException(nameof(playlistFiles));
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        Playlists.CollectionChanged += OnPlaylistsCollectionChanged;
    }

    internal PlaylistsViewModel(Func<Playlist, PlaylistViewModel> factory)
        : this(factory, new NullPlaybackService(), new NullFileDialogService(), new NullLibraryScannerService(),
               new NullLibraryCache(), new NullMetadataReader(), new NullPlaylistFileService()) { }
```

在文件末尾 Null 桩区（`NullMetadataReader` 之后）加：

```csharp
    private sealed class NullPlaylistFileService : IPlaylistFileService
    {
        public Task<PlaylistImportResult> ImportAsync(string playlistFilePath) =>
            Task.FromResult(new PlaylistImportResult("导入的歌单", Array.Empty<string>(), 0, 0, 0));

        public Task ExportAsync(string destPath, IReadOnlyList<Track> tracks) => Task.CompletedTask;
    }
```

- [x] **Step 4: 实现容器级导入**

在 `ImportFolderAsync` 之后追加：

```csharp
    // —— Phase 18: 播放列表文件导入（容器级 = 新建歌单） ——

    /// <summary>
    /// 导入 .m3u/.m3u8/.pls → **新建**一个普通歌单并设为当前查看项（与 ImportFolderAsync 同层级语义）。
    ///
    /// presetPath 非空 = 拖拽入口；null = 走文件对话框。返回 null 表示用户取消。
    /// SourceFolder 刻意留 null：导入的歌单不是文件夹绑定歌单，不写 library cache，
    /// 重启后由 Hydrate 走 LoadMetadataForNormalPlaylistSync 读文件元数据。
    /// </summary>
    public async Task<PlaylistImportReport?> ImportPlaylistFileAsync(string? presetPath = null)
    {
        var path = presetPath ?? _fileDialog.OpenFiles(PlaylistFileFormats.OpenFilter).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(path)) return null;

        var sourceFile = Path.GetFileName(path);
        // ConfigureAwait(true)：下面要动 ObservableCollection（WPF 要求 UI 线程）
        var result = await _playlistFiles.ImportAsync(path).ConfigureAwait(true);

        if (result.AcceptedPaths.Count == 0)
        {
            return new PlaylistImportReport(sourceFile, null, false, 0,
                result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
        }

        var tracks = await _scanner.ReadMetadataBatchAsync(result.AcceptedPaths).ConfigureAwait(true);
        if (tracks.Count == 0)
        {
            // 条目都存在但元数据一条都没读出来 —— 不建空歌单，报告为"没导入"
            return new PlaylistImportReport(sourceFile, null, false, 0,
                result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
        }

        var seed = new Playlist(
            Id: Guid.NewGuid().ToString(),
            Name: result.SuggestedName,
            Items: result.AcceptedPaths,
            CurrentIndex: -1,
            ShuffleEnabled: false,
            RepeatMode: RepeatMode.Off,
            SourceFolder: null);

        var vm = _factory(seed);
        // 必须 Clear：PlaylistViewModel 构造器会用 File.Exists 过滤 seed.Items 并预填占位 Track，
        // 这里要用带真实元数据的 Track 顶掉它们（与 ImportFolderAsync 同一手法）。
        vm.Queue.Clear();
        foreach (var track in tracks)
            vm.Queue.Add(track);

        HookPlaylistVm(vm);
        Playlists.Add(vm);        // CollectionChanged → StateChanged → MainViewModel debounce save
        ViewedPlaylist = vm;

        return new PlaylistImportReport(sourceFile, result.SuggestedName, true, tracks.Count,
            result.SkippedMissing, result.SkippedUnsupported, result.TotalEntries);
    }
```

- [x] **Step 5: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistsViewModelTests"`
Expected: 全通过（原有 + 新增 5 例）。

Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 全绿；`dotnet build D-player.sln -c Debug --nologo -v q` → 0 错误 0 警告。

- [x] **Step 6: 提交**

```bash
git add ViewModels/PlaylistsViewModel.cs Tests/ViewModels/PlaylistsViewModelTests.cs
git commit -m "feat(vm): container-level playlist import creates a normal playlist (Phase 18)

- PlaylistsViewModel.ImportPlaylistFileAsync mirrors ImportFolderAsync:
  batch metadata read, factory-built VM, hooked, added, set as viewed
- SourceFolder stays null so imported playlists skip the library cache and
  load through LoadMetadataForNormalPlaylistSync on next start
- No playlist is created when every entry was skipped or when the metadata
  batch comes back empty; existing Null-stub test ctor keeps working via a
  separate mock-based helper"
```

---

### Task 8: View 层基础设施（ShowInfo / 拖拽过滤 / 图标 / 文案 / 执行器）

**Files:**
- Modify: `Views/Dialogs/ConfirmDialog.xaml.cs`
- Modify: `Views/Controls/DragDropExtensions.cs`
- Modify: `Themes/Icons.xaml`
- Create: `Views/Controls/PlaylistImportReportFormatter.cs`
- Create: `Views/Controls/PlaylistImportUi.cs`
- Test: `Tests/Views/PlaylistImportReportFormatterTests.cs`

**Interfaces:**
- Consumes: `PlaylistImportReport`（Task 6）、`PlaylistFileFormats`（Task 2）、`ConfirmDialog.ShowCore`
- Produces:
  - `ConfirmDialog.ShowInfo(Window? owner, string title, string message) → void`
  - `DragDropExtensions.PlaylistFileExtensions`（`IReadOnlyList<string>`）、`DragDropExtensions.FilterPlaylistPaths(IEnumerable<string>?) → IReadOnlyList<string>`
  - `PlaylistImportReportFormatter.Format(IReadOnlyList<PlaylistImportReport>) → string`
  - `PlaylistImportUi.RunDialogAsync(Func<string?, Task<PlaylistImportReport?>>, Window?) → Task`、`PlaylistImportUi.RunForDroppedFilesAsync(Func<string?, Task<PlaylistImportReport?>>, Window?, IReadOnlyList<string>) → Task`
  - 资源键 `Icon.Import`、`Icon.Export`

- [x] **Step 1: 写文案失败测试**

创建 `Tests/Views/PlaylistImportReportFormatterTests.cs`：

```csharp
using System;
using System.Collections.Generic;
using DPlayer.ViewModels;
using DPlayer.Views.Controls;
using Xunit;

namespace DPlayer.Tests.Views;

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
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistImportReportFormatterTests"`
Expected: 编译失败 —— `PlaylistImportReportFormatter` 不存在（CS0246）。

- [x] **Step 3: 实现文案格式化**

创建 `Views/Controls/PlaylistImportReportFormatter.cs`（文案逐字符对齐设计稿 §9.3，也就是 Step 1 测试里的期望字符串）：

```csharp
using System.Collections.Generic;
using System.Text;
using DPlayer.ViewModels;

namespace DPlayer.Views.Controls;

/// <summary>
/// 把 VM 返回的结构化导入报告拼成中文提示文案（Phase 18）。
///
/// 放 View 层的理由：侧边栏按钮、侧边栏拖拽、歌单工具栏按钮、列表区拖拽四个入口
/// 共用同一套文案规则；VM 只提供数据（分层纪律：VM 不拼展示字符串）。
/// 纯字符串函数，无 WPF 依赖，因此可单测。
/// </summary>
public static class PlaylistImportReportFormatter
{
    /// <summary>对话框标题（所有入口统一）。</summary>
    public const string DialogTitle = "导入播放列表";

    public static string Format(IReadOnlyList<PlaylistImportReport> reports)
    {
        if (reports is null || reports.Count == 0) return string.Empty;
        return reports.Count == 1 ? FormatSingle(reports[0]) : FormatMany(reports);
    }

    private static string FormatSingle(PlaylistImportReport r)
    {
        if (!r.AnyImported)
        {
            return r.Skipped == 0
                ? $"「{r.SourceFile}」没有可导入的条目"
                : $"「{r.SourceFile}」没有可导入的条目\n{SkippedLine(r)}";
        }

        var target = r.CreatedNewPlaylist ? $"新歌单「{r.PlaylistName}」" : $"歌单「{r.PlaylistName}」";
        var second = r.Skipped == 0 ? $"{r.Imported} 首入列" : $"{r.Imported} 首入列，{SkippedLine(r)}";

        return $"已导入「{r.SourceFile}」→ {target}\n{second}";
    }

    private static string FormatMany(IReadOnlyList<PlaylistImportReport> reports)
    {
        var sb = new StringBuilder();
        sb.Append("已导入 ").Append(reports.Count).Append(" 个播放列表");

        foreach (var r in reports)
        {
            sb.Append('\n').Append("· ").Append(r.SourceFile).Append(" → ");

            if (!r.AnyImported)
            {
                sb.Append("没有可导入的条目");
            }
            else
            {
                sb.Append(r.CreatedNewPlaylist ? "新歌单「" : "歌单「")
                  .Append(r.PlaylistName).Append("」：").Append(r.Imported).Append(" 首");
            }

            if (r.Skipped > 0) sb.Append("（跳过 ").Append(r.Skipped).Append("）");
        }

        return sb.ToString();
    }

    /// <summary>"跳过 N 条（文件缺失 X / 格式不支持 Y）"——调用方决定前面接换行还是逗号。</summary>
    private static string SkippedLine(PlaylistImportReport r) =>
        $"跳过 {r.Skipped} 条（文件缺失 {r.SkippedMissing} / 格式不支持 {r.SkippedUnsupported}）";
}
```

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet test D-player.sln -c Debug --nologo -v q --filter "FullyQualifiedName~PlaylistImportReportFormatterTests"`
Expected: 5 通过 / 0 失败。若某个断言因全角/半角标点不一致失败，**改实现去对齐测试**（测试里的字符串就是设计稿 §9.3 的文案）。

- [x] **Step 5: `ConfirmDialog.ShowInfo`**

在 `Views/Dialogs/ConfirmDialog.xaml.cs` 的 `ShowError` 之后追加，并把类注释里的模式说明补上 ShowInfo：

```csharp
    /// <summary>模态显示信息提示（仅确定按钮；Esc 可关闭）。与 ShowError 同形状，语义为"结果告知"而非错误。</summary>
    public static void ShowInfo(Window? owner, string title, string message)
        => ShowCore(owner, title, message, showCancel: false);
```

- [x] **Step 6: `DragDropExtensions` 加播放列表过滤**

在 `Views/Controls/DragDropExtensions.cs` 顶部 using 区加 `using DPlayer.Services.PlaylistFiles;`，在 `AudioExtensions` / `FilterAudioPaths` 之后追加：

```csharp
    /// <summary>支持的播放列表后缀（Phase 18）。代理到 PlaylistFileFormats，单一来源避免漂移。</summary>
    public static readonly IReadOnlyList<string> PlaylistFileExtensions = PlaylistFileFormats.Extensions;

    /// <summary>过滤出播放列表文件（.m3u/.m3u8/.pls，大小写不敏感）。文件夹自动剔除。</summary>
    public static IReadOnlyList<string> FilterPlaylistPaths(IEnumerable<string>? paths)
    {
        if (paths is null) return Array.Empty<string>();

        var result = new List<string>();
        foreach (var path in paths)
        {
            if (PlaylistFileFormats.IsPlaylistFile(path)) result.Add(path);
        }
        return result;
    }
```

同时把类注释里"AudioExtensions: 代理到 Models.AudioConstants…"那段补一句：Phase 18 起 Drop 目标必须**同时**查 `FilterAudioPaths` 与 `FilterPlaylistPaths`，两个白名单互不重叠。

- [x] **Step 7: 两个新图标**

在 `Themes/Icons.xaml` 的 `Icon.MusicNote` 之后、`Icon.PlayMarker` 之前追加（Lucide download/upload，转成本文件的绝对坐标风格）：

```xml
    <!-- 播放列表文件导入/导出（Phase 18）：Lucide download / upload -->
    <Geometry x:Key="Icon.Import">M21,15 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V15 M7,10 L12,15 L17,10 M12,15 V3</Geometry>
    <Geometry x:Key="Icon.Export">M21,15 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V15 M17,8 L12,3 L7,8 M12,3 V15</Geometry>
```

- [x] **Step 8: 共用执行器**

创建 `Views/Controls/PlaylistImportUi.cs`：

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using DPlayer.ViewModels;
using DPlayer.Views.Dialogs;

namespace DPlayer.Views.Controls;

/// <summary>
/// 播放列表导入的 View 层共用执行器（Phase 18）。
///
/// 四个入口（侧边栏按钮 / 侧边栏拖拽 / 工具栏按钮 / 列表区拖拽）共用同一套
/// "跑导入 → 聚合报告 → 弹信息框 → 兜异常" 流程。异常必须在这里兜住：
/// 调用方是 async void 事件处理器，未观察异常会直接崩进程。
/// </summary>
public static class PlaylistImportUi
{
    /// <summary>对话框入口：importOne 传 null 表示让 VM 自己弹文件对话框。</summary>
    public static async Task RunDialogAsync(
        Func<string?, Task<PlaylistImportReport?>> importOne, Window? owner)
    {
        try
        {
            var report = await importOne(null).ConfigureAwait(true);
            if (report is null) return;                 // 用户取消 → 不弹任何框

            ConfirmDialog.ShowInfo(owner, PlaylistImportReportFormatter.DialogTitle,
                PlaylistImportReportFormatter.Format(new[] { report }));
        }
        catch (Exception ex)
        {
            ConfirmDialog.ShowError(owner, PlaylistImportReportFormatter.DialogTitle, $"导入失败：{ex.Message}");
        }
    }

    /// <summary>拖拽入口：逐个导入（每个文件一个歌单/一次追加），聚合成一份报告。</summary>
    public static async Task RunForDroppedFilesAsync(
        Func<string?, Task<PlaylistImportReport?>> importOne, Window? owner, IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0) return;

        try
        {
            var reports = new List<PlaylistImportReport>(paths.Count);
            foreach (var path in paths)
            {
                var report = await importOne(path).ConfigureAwait(true);
                if (report is not null) reports.Add(report);
            }
            if (reports.Count == 0) return;

            ConfirmDialog.ShowInfo(owner, PlaylistImportReportFormatter.DialogTitle,
                PlaylistImportReportFormatter.Format(reports));
        }
        catch (Exception ex)
        {
            ConfirmDialog.ShowError(owner, PlaylistImportReportFormatter.DialogTitle, $"导入失败：{ex.Message}");
        }
    }
}
```

- [x] **Step 9: 构建 + 全量测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: 全绿。

- [x] **Step 10: 转 CRLF 并提交**

```bash
unix2dos Views/Controls/PlaylistImportReportFormatter.cs Views/Controls/PlaylistImportUi.cs Tests/Views/PlaylistImportReportFormatterTests.cs
git add Views/Dialogs/ConfirmDialog.xaml.cs Views/Controls/DragDropExtensions.cs Views/Controls/PlaylistImportReportFormatter.cs Views/Controls/PlaylistImportUi.cs Themes/Icons.xaml Tests/Views/PlaylistImportReportFormatterTests.cs
git commit -m "feat(view): import report plumbing for playlist files (Phase 18)

- ConfirmDialog.ShowInfo: single-button info variant (an import summary is
  not an error)
- PlaylistImportReportFormatter turns the VM's structured report into the
  copy from the spec, shared by all four entry points
- PlaylistImportUi runs the import, aggregates reports and swallows
  exceptions so async void handlers cannot crash the process
- DragDropExtensions.FilterPlaylistPaths proxies PlaylistFileFormats;
  Icon.Import / Icon.Export added"
```

---

### Task 9: 侧边栏入口（导入 → 新建歌单）

**Files:**
- Modify: `Views/Controls/PlaylistsSidebarView.xaml`
- Modify: `Views/Controls/PlaylistsSidebarView.xaml.cs`

**Interfaces:**
- Consumes: `PlaylistsViewModel.ImportPlaylistFileAsync`（Task 7）、`PlaylistImportUi`（Task 8）、`DragDropExtensions.FilterPlaylistPaths`、`Icon.Import`
- Produces: 侧边栏第三个按钮 `ImportBtn` + `ImportBtn_Click`；`PlaylistList_DragOver` / `PlaylistList_Drop` 支持外部播放列表文件。

- [x] **Step 1: XAML 加按钮**

在 `Views/Controls/PlaylistsSidebarView.xaml` 的 `RemoveBtn` 结束标签之后、`</StackPanel>` 之前插入：

```xml
            <Button x:Name="ImportBtn"
                    Width="28" Height="28"
                    Padding="0"
                    Margin="6,0,0,0"
                    Foreground="{StaticResource ForegroundPrimary}"
                    ToolTip="导入播放列表（M3U / M3U8 / PLS）→ 新建歌单"
                    Click="ImportBtn_Click">
                <Path Style="{StaticResource IconPath}" Width="14" Height="14"
                      Data="{StaticResource Icon.Import}"
                      Stroke="{StaticResource ForegroundPrimary}"/>
            </Button>
```

- [x] **Step 2: code-behind 加 Click 处理器**

在 `Views/Controls/PlaylistsSidebarView.xaml.cs` 的 `RemoveBtn_Click` 之后插入：

```csharp
    /// <summary>导入播放列表文件 → 新建歌单（容器级语义，Phase 18）。</summary>
    private async void ImportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        await PlaylistImportUi.RunDialogAsync(_vm.ImportPlaylistFileAsync, Window.GetWindow(this));
    }
```

- [x] **Step 3: DragOver 支持外部播放列表文件**

把 `PlaylistList_DragOver` 的 `else` 分支替换为两个分支（内部重排分支不动）：

```csharp
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 外部文件：只认播放列表文件；音频文件落在侧边栏无意义（那是列表区的活）
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var lists = DragDropExtensions.FilterPlaylistPaths(paths);
            e.Effects = lists.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
```

- [x] **Step 4: Drop 支持外部播放列表文件**

在 `PlaylistList_Drop` 的 `try` 块里，内部重排分支之后加 `else if`（`finally` 不动）：

```csharp
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                var lists = DragDropExtensions.FilterPlaylistPaths(paths);
                if (lists.Count > 0)
                {
                    // 不 await：Drop 是同步 void 处理器，导入进度由执行器内部弹框收尾。
                    // 异常已在 PlaylistImportUi 内兜住。
                    _ = PlaylistImportUi.RunForDroppedFilesAsync(
                        _vm.ImportPlaylistFileAsync, Window.GetWindow(this), lists);
                }
            }
```

- [x] **Step 5: 构建 + 全量测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告（XAML 里 `Icon.Import` 键存在、`ImportBtn_Click` 签名匹配）。

Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 全绿（UI 行为在 Task 11 用 GUI 验收）。

- [x] **Step 6: 提交**

```bash
git add Views/Controls/PlaylistsSidebarView.xaml Views/Controls/PlaylistsSidebarView.xaml.cs
git commit -m "feat(view): sidebar playlist-file import creates a new playlist (Phase 18)

- Third 28x28 icon button (Icon.Import) next to + / -
- PlaylistList DragOver/Drop now accept external .m3u/.m3u8/.pls as Copy
  while internal reorder keeps priority; no insertion adorner for files
- Multiple dropped lists are imported one by one into a single aggregated
  report dialog"
```

---

### Task 10: 歌单工具栏入口（导入追加 / 导出）+ 列表区拖拽分流

**Files:**
- Modify: `Views/Controls/PlaylistView.xaml`
- Modify: `Views/Controls/PlaylistView.xaml.cs`

**Interfaces:**
- Consumes: `PlaylistViewModel.ImportPlaylistFileAsync` / `ExportPlaylistFileAsync`（Task 6）、`PlaylistImportUi`、`DragDropExtensions.FilterPlaylistPaths`、`ConfirmDialog.ShowError`、`Icon.Import` / `Icon.Export`
- Produces: 工具栏"导入列表"/"导出列表"两个按钮及其处理器；`QueueList_DragOver` / `QueueList_Drop` / `Root_DragEnter` / `Root_DragOver` / `Root_Drop` 的播放列表文件分流。

- [x] **Step 1: XAML 加两个按钮**

在 `Views/Controls/PlaylistView.xaml` 工具栏左侧 `StackPanel` 中，"导入文件夹"按钮之后、"清空"按钮之前插入：

```xml
                <Button Padding="12,4" Margin="8,0,0,0" Click="ImportListButton_Click"
                        ToolTip="导入 M3U / M3U8 / PLS，追加到当前歌单">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource IconPath}" Width="12" Height="12"
                              Data="{StaticResource Icon.Import}"
                              Stroke="{StaticResource ForegroundPrimary}"/>
                        <TextBlock Text="导入列表" FontSize="12" Margin="4,0,0,0"/>
                    </StackPanel>
                </Button>
                <Button Padding="12,4" Margin="8,0,0,0" Click="ExportListButton_Click"
                        ToolTip="把当前歌单导出为 .m3u8">
                    <StackPanel Orientation="Horizontal">
                        <Path Style="{StaticResource IconPath}" Width="12" Height="12"
                              Data="{StaticResource Icon.Export}"
                              Stroke="{StaticResource ForegroundPrimary}"/>
                        <TextBlock Text="导出列表" FontSize="12" Margin="4,0,0,0"/>
                    </StackPanel>
                </Button>
```

- [x] **Step 2: code-behind 加两个 Click 处理器**

在 `Views/Controls/PlaylistView.xaml.cs` 的 `ClearButton_Click` 之后插入（文件已有 `using DPlayer.Views.Dialogs;`）：

```csharp
    /// <summary>导入播放列表文件 → 追加到当前歌单（歌单级语义，Phase 18）。</summary>
    private async void ImportListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        await PlaylistImportUi.RunDialogAsync(_vm.ImportPlaylistFileAsync, Window.GetWindow(this));
    }

    /// <summary>导出当前歌单为 .m3u8。空队列静默返回（与清空按钮同处理方式）。</summary>
    private async void ExportListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (_vm.Queue.Count == 0) return;

        try
        {
            var error = await _vm.ExportPlaylistFileAsync();
            if (error is not null)
                ConfirmDialog.ShowError(Window.GetWindow(this), "导出播放列表", error);
        }
        catch (Exception ex)
        {
            // VM 已经捕获了服务异常；这里兜的是对话框/线程等意外
            ConfirmDialog.ShowError(Window.GetWindow(this), "导出播放列表", $"导出失败：{ex.Message}");
        }
    }
```

- [x] **Step 3: 列表区 DragOver / DragEnter 认播放列表文件**

把 `QueueList_DragOver` 的外部文件分支替换为：

```csharp
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 外部文件：音频 → 入队；播放列表文件 → 导入追加（Phase 18）
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            var lists = DragDropExtensions.FilterPlaylistPaths(paths);
            e.Effects = (audio.Count > 0 || lists.Count > 0) ? DragDropEffects.Copy : DragDropEffects.None;
        }
```

把 `Root_DragEnter` 里"只在含白名单音频时高亮"的判定改为：

```csharp
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        var audio = DragDropExtensions.FilterAudioPaths(paths);
        var lists = DragDropExtensions.FilterPlaylistPaths(paths);
        if (audio.Count == 0 && lists.Count == 0) return;

        DragDropExtensions.SetIsDragOver(QueueListBorder, true);
```

（同时把该方法上方注释里"全非音频…不亮"改成"全非音频且无播放列表文件…不亮"。）

把 `Root_DragOver` 的 FileDrop 分支改为：

```csharp
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            var audio = DragDropExtensions.FilterAudioPaths(paths);
            var lists = DragDropExtensions.FilterPlaylistPaths(paths);
            e.Effects = (audio.Count > 0 || lists.Count > 0) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
```

- [x] **Step 4: 两个 Drop 处理器分流**

`QueueList_Drop` 的外部文件分支替换为：

```csharp
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];

                var audio = DragDropExtensions.FilterAudioPaths(paths);
                if (audio.Count > 0)
                {
                    _vm.DropExternalFilesCommand.Execute(audio);
                }

                // 播放列表文件走导入管道（追加语义）。混合拖入时两条都跑。
                var lists = DragDropExtensions.FilterPlaylistPaths(paths);
                if (lists.Count > 0)
                {
                    _ = PlaylistImportUi.RunForDroppedFilesAsync(
                        _vm.ImportPlaylistFileAsync, Window.GetWindow(this), lists);
                }
            }
```

`Root_Drop` 的第 3 段（落在 Border 内但 ListBox 之外）替换为：

```csharp
        if (_vm is null) return;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];

            var audio = DragDropExtensions.FilterAudioPaths(paths);
            if (audio.Count > 0)
            {
                _vm.DropExternalFilesCommand.Execute(audio);
            }

            var lists = DragDropExtensions.FilterPlaylistPaths(paths);
            if (lists.Count > 0)
            {
                _ = PlaylistImportUi.RunForDroppedFilesAsync(
                    _vm.ImportPlaylistFileAsync, Window.GetWindow(this), lists);
            }

            e.Handled = true;
        }
```

- [x] **Step 5: 构建 + 全量测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

Run: `dotnet test D-player.sln -c Debug --nologo -v q` → 全绿。

- [x] **Step 6: 提交**

```bash
git add Views/Controls/PlaylistView.xaml Views/Controls/PlaylistView.xaml.cs
git commit -m "feat(view): playlist toolbar import/export + drop routing (Phase 18)

- Toolbar order is now 添加 / 导入文件夹 / 导入列表 / 导出列表 / 清空, keeping
  the destructive action last
- Export silently no-ops on an empty queue (same treatment as 清空) and
  surfaces service failures through ConfirmDialog.ShowError
- DragOver/DragEnter/Drop on both the ListBox and the root Border accept
  playlist files alongside audio; mixed drops run both pipelines"
```

---

### Task 11: 文档同步 + 全量验证 + GUI 验收

**Files:**
- Modify: `docs/PROJECT.md`、`docs/COUPLING.md`、`README.md`
- Modify: `docs/superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md`（§8 口径修正 + 状态改"已实现"）
- Modify: 本计划文件（勾选所有步骤）

**Interfaces:**
- Consumes: Task 1–10 的最终代码形态（写文档前用 `git log --stat` 与实读代码核对，不要凭记忆）
- Produces: 文档与代码一致；GUI 验收记录。

- [x] **Step 1: 全量构建与测试**

Run: `dotnet build D-player.sln -c Debug --nologo -v q`
Expected: 0 错误 0 警告。

Run: `dotnet test D-player.sln -c Debug --nologo -v q`
Expected: 全绿。**记下实际通过数**（基线 96 + 本阶段新增 59 = 155 左右；以实际输出为准），下一步文档里要写真实数字。

- [x] **Step 2: 更新 `docs/PROJECT.md`**

- 目录树：在 `Services/` 下加 `PlaylistFiles/` 子树（7 个新文件），`ViewModels/` 下加 `PlaylistImportReport.cs`，`Views/Controls/` 下加 `PlaylistImportReportFormatter.cs` / `PlaylistImportUi.cs`，`Tests/` 下加 `Views/`。
- 新增 Phase 18 小节：服务层职责与"读侧不抛/写侧抛"的不对称、双入口分层（容器级新建 vs 歌单级追加）、`presetPath` 是拖拽与对话框的共用接点、编码探测链、导出的 M3U8 形状。
- 若 PROJECT.md 有测试计数/阶段状态一类的汇总处，同步为新数字。

- [x] **Step 3: 更新 `docs/COUPLING.md`**

§5 追加"Phase 18 新增"块，逐条登记（措辞与设计稿 §11 一致，第 2 条按实际实现改为静态构造函数口径）：

1. **拖拽双轨白名单**：`FilterAudioPaths` 与 `FilterPlaylistPaths` 互不重叠，Drop 处理器必须两个都查，否则 `.m3u` 会被静默丢弃（回到 Phase 18 之前的行为）。
2. **CodePages provider 注册点**：在 `PlaylistFileEncoding` 的静态构造函数里，`Encoding.GetEncoding(936)` 只出现在该类内部，所以注册永远早于解码。**不要**把 GBK 解码搬到别的类型里——搬走就等于把注册时机重新变成一条口头约定。
3. **Import 不抛 / Export 抛**：`ImportAsync` 吞掉 IO/权限/路径异常返回空结果，`ExportAsync` 必须让异常冒到 VM 转文案。
4. **`#EXTINF` / PLS `Title=` 刻意忽略**：标题与时长只信音频文件；要用列表文件的元数据得先定优先级规则。
5. **导入报告文案由 View 组装**：VM 只返回 `PlaylistImportReport`，四个入口共用 `PlaylistImportReportFormatter`。
6. **`SourceFolder = null` 是导入歌单的身份标记**：设成非 null 会被当作文件夹绑定歌单，触发 cache 读写与"刷新文件夹"按钮。
7. **`DropExternalFilesCommand` 的"paths 已过滤"契约新增调用方**：原本只有 View 拖拽入口，Phase 18 起 `PlaylistViewModel.ImportPlaylistFileAsync` 也会调用它；任何新增调用方必须自己保证路径已过滤。

§7 don't-do 追加两条：

- ❌ 不要为导入给 `PlaylistViewModel` 注入 `ILibraryScannerService`——追加路径复用既有 `DropExternalFiles`。
- ❌ 不要把导出入口放到侧边栏——侧边栏作用于"选中项"，导出语义是"当前查看的歌单"，两个指针在键盘导航下可能不同步。

- [x] **Step 4: 更新 `README.md`**

功能列表加一条：播放列表导入（M3U / M3U8 / PLS，支持相对路径与 GBK/UTF-8/UTF-16 编码）与导出（M3U8，绝对路径 + `#EXTINF`）。构建/测试命令段落里的测试数字同步。

- [x] **Step 5: 修正设计稿 §8 与状态**

- §8 改为：provider 在 `PlaylistFileEncoding` 静态构造函数注册，`App.xaml.cs` 不需改动；保留"重复注册无害"的说明，删掉"必须在 OnStartup 最早期"的要求。
- 顶部状态 `**待实现**` → `**已实现**`，并在决策记录表补一行：#8 CodePages 注册点从 App 启动改为类型静态构造（理由：把启动顺序契约变成类型不变量）。

- [x] **Step 6: 勾选本计划所有步骤**

把本文件所有 `- [ ]` 改成 `- [x]`（用 Edit 工具逐处改，**不要**用 `sed -i`：它会剥掉 CRLF）。改完确认：

Run: `git ls-files --eol docs/superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md`
Expected: `i/lf    w/crlf`。若是 `w/lf`，执行 `unix2dos <该文件>` 后重新 `git add`。

- [ ] **Step 7: GUI 验收（ComputerUse）**

> **执行记录（Task 11 文档部分）**：本步骤由 controller 另行执行并留档，不在文档同步提交（Step 8）范围内；故此处保留未勾选，待 GUI 走查完成后由执行方勾选。

先 `dotnet build D-player.sln -c Debug` 后启动 exe（或 `dotnet run --project D-player.csproj`）。

用 **Windows PowerShell 5.1**（`powershell.exe`，不是 pwsh —— .NET Framework 自带 936 编码，不需要注册 CodePages provider）造样本。把 `$music` 换成你本机真实曲库目录，样本才有"存在"的条目可导入：

```powershell
$dir   = Join-Path $env:TEMP 'dplayer-p18'
$music = 'D:\Music'                                    # ← 改成真实曲库；里面至少要有 1 首 .mp3
New-Item -ItemType Directory -Force -Path $dir, (Join-Path $dir '音乐') | Out-Null

$utf8  = New-Object System.Text.UTF8Encoding($false)
$gbk   = [System.Text.Encoding]::GetEncoding(936)
$a     = (Get-ChildItem -Path $music -Filter *.mp3 -File | Select-Object -First 1).FullName
$cn    = Join-Path $dir '音乐\晴天.mp3'
Copy-Item $a $cn -Force                                # 让中文路径样本真的有文件可命中

# 1) utf8.m3u8 —— 绝对路径 + #EXTINF
[IO.File]::WriteAllText((Join-Path $dir 'utf8.m3u8'), "#EXTM3U`r`n#EXTINF:200,Artist - Title`r`n$a`r`n", $utf8)
# 2) rel.m3u —— 相对该文件所在目录
[IO.File]::WriteAllText((Join-Path $dir 'rel.m3u'), "音乐/晴天.mp3`r`n", $utf8)
# 3) gbk.m3u —— GBK 编码 + 中文相对路径（走编码回退分支）
[IO.File]::WriteAllText((Join-Path $dir 'gbk.m3u'), "音乐\晴天.mp3`r`n", $gbk)
# 4) radio.pls —— 网络流 + 真实文件 + 不存在的文件（三种桶各归其位）
[IO.File]::WriteAllText((Join-Path $dir 'radio.pls'), "[playlist]`r`nFile1=http://stream.example.com/radio`r`nTitle1=Radio`r`nLength1=-1`r`nFile2=$a`r`nFile3=$dir\music\gone.mp3`r`nNumberOfEntries=3`r`nVersion=2`r`n", $utf8)
# 5) dead.m3u —— 全部条目都不可用（1 个缺失 + 1 个后缀不在白名单）
[IO.File]::WriteAllText((Join-Path $dir 'dead.m3u'), "$dir\music\nope1.mp3`r`n$dir\music\nope2.ogg`r`n", $utf8)

Get-ChildItem $dir | Select-Object Name, Length
```

`dead.m3u` 故意混了 `.mp3`（不存在 → 文件缺失）与 `.ogg`（存在与否都算格式不支持）——顺带验证"后缀判定先于存在性判定"的桶归属。

逐项走查（每项截图留档）：

1. 侧边栏第三个按钮导入 `utf8.m3u8` → 新歌单出现、成为当前查看项、曲目有正确标题/艺术家/时长；报告框文案正确。
2. 工具栏"导入列表"导入 `gbk.m3u` → 追加到当前歌单末尾，中文路径不乱码。
3. 导入 `rel.m3u` → 相对路径正确解析（条目数不为 0）。
4. 导入 `radio.pls` → 报告显示跳过计数（格式不支持含网络流、文件缺失各归其桶）。
5. 导入 `dead.m3u` → 侧边栏入口不新建歌单、报告显示"没有可导入的条目"。
6. 拖 `utf8.m3u8` 到侧边栏 → 新建歌单；拖到歌单列表区 → 追加当前歌单；拖"音频 + m3u"混合到列表区 → 两条管道都生效。
7. 取消文件对话框 → 不弹任何框。
8. "导出列表" → 保存对话框默认文件名 = 歌单名；导出后记事本核对 `#EXTM3U` / `#EXTINF:秒,艺术家 - 标题` / 绝对路径；把导出文件再导入 → 队列与原歌单一致（往返）。
9. 空歌单点"导出列表" → 无反应（不弹框）。
10. 导出失败路径：把目标文件设为只读或让目标被占用后导出 → 弹主题化 `ShowError`，**不是**系统 MessageBox。
11. 重启应用 → 导入的歌单仍在、元数据正常（说明走的是普通歌单加载路径而非文件夹缓存路径）。
12. 回归：清空/删除曲目/删除歌单的确认框、进度条点击跳转、频谱、EQ、设置保存仍正常。

Run: `grep -rn "MessageBox" --include=*.cs . | grep -v "/obj/\|/bin/"`
Expected: 无输出（全应用仍无系统 MessageBox）。

- [x] **Step 8: 提交**

```bash
unix2dos docs/PROJECT.md docs/COUPLING.md README.md docs/superpowers/specs/2026-10-05-d-player-phase18-playlist-file-io-design.md docs/superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md
git add docs/PROJECT.md docs/COUPLING.md README.md docs/superpowers/
git commit -m "docs: Phase 18 playlist file I/O - PROJECT/COUPLING/README + spec status

- PROJECT.md: Services/PlaylistFiles module, two-tier import entry points,
  encoding probe chain, M3U8 export shape, updated test count
- COUPLING.md §5: seven new implicit contracts (dual drop whitelists,
  CodePages registration point, import-never-throws vs export-throws,
  #EXTINF deliberately ignored, View-owned report copy, SourceFolder=null
  as the imported-playlist marker, new DropExternalFiles caller)
- COUPLING.md §7: two new don't-do entries
- Spec §8 corrected to the static-ctor registration; status -> 已实现"
```

---

## Self-Review 记录

**1. 设计稿覆盖**

| 设计稿章节 | 实现于 |
|-----------|--------|
| §3 文件清单 | 全部落在 Task 1–10 的 Files 段（含 7 个新服务文件、2 个新 View 文件、1 个新 VM record） |
| §4 服务层契约（含错误策略不对称） | Task 3（接口 + Import 不抛）、Task 4（Export 抛） |
| §5.1 取什么（忽略 `#EXTINF` / `Title=`） | Task 2 Step 4 两个解析器 + 测试 |
| §5.2 路径归一化 | Task 3 `Classify` + 3 个测试 |
| §5.3 编码探测 | Task 1 全部 |
| §5.4 过滤与计数（URL/后缀/存在性顺序、不去重） | Task 3 `Classify` + 4 个测试 |
| §6.1 SaveFile | Task 5 |
| §6.2 M3U8 文本（无 BOM/CRLF/`-1`/Artist 空/文件名清洗） | Task 4（writer + 4 测试）、Task 6（`SanitizeFileName` + 测试） |
| §7.1 报告 record | Task 6 Step 3 |
| §7.2 方法而非命令 | Task 6 Step 5、Task 7 Step 4 |
| §7.3 新建路径 7 步 | Task 7 Step 4（含 `SourceFolder = null`、`tracks.Count == 0` 不建歌单） |
| §7.4 追加路径（复用 `DropExternalFiles`） | Task 6 Step 5 |
| §7.5 导出路径 | Task 6 Step 5 + Task 10 Step 2 |
| §8 依赖与启动 | Task 1 Step 1–2、Step 5（**偏离**：注册点移到静态构造函数，Task 11 Step 5 回写设计稿） |
| §9.1 按钮与图标 | Task 8 Step 7、Task 9 Step 1、Task 10 Step 1 |
| §9.2 拖拽 | Task 8 Step 6、Task 9 Step 3–4、Task 10 Step 3–4 |
| §9.3 报告文案 + `ShowInfo` | Task 8 Step 3、Step 5 |
| §10.1 服务层 22 例 | Task 1（6）+ Task 2（16）+ Task 3（14）+ Task 4（5）= 41 例——超出设计稿清单，覆盖其全部条目 |
| §10.2 VM 测试 | Task 6（8）+ Task 7（5） |
| §10.3 桩补齐 | Task 5 Step 3、Task 7 Step 3 |
| §11 文档同步 | Task 11 Step 2–5 |
| §12 验收标准 | Task 11 Step 1、Step 7 |
| §13 风险与边界 | GBK 误判（Task 1 注释）、覆盖询问（Task 5 `OverwritePrompt`）、`async void` 兜异常（Task 8 `PlaylistImportUi`）、内部格式优先级（Task 9/10 Drop 分支顺序）、混合拖入报告（Task 8 `FormatMany`） |

无遗漏项。

**2. 占位符扫描**：无 TBD / TODO / "类似 Task N" / "补充适当的错误处理"；每个写代码的步骤都给了完整可编译的代码块，每个验证步骤都给了确切命令与期望值。`ExportAsync` 不设过渡占位实现（接口声明与实现同在 Task 4），所以任何一次提交都不含半成品成员。计划比设计稿多出 `PlaylistImportReportFormatter` 的 5 个文案测试（Task 8 Step 1）——文案被四个入口共用，值得用测试锁死。

**3. 类型一致性**：`PlaylistImportResult`（5 个位置参数，序为 `SuggestedName, AcceptedPaths, TotalEntries, SkippedMissing, SkippedUnsupported`）在 Task 3 定义，Task 6/7 的测试 helper `ImportResult(...)` 按同序构造；`PlaylistImportReport`（7 参 + `Skipped` / `AnyImported` 两个派生属性，序为 `SourceFile, PlaylistName, CreatedNewPlaylist, Imported, SkippedMissing, SkippedUnsupported, TotalEntries`）在 Task 6 定义，Task 7 的生产代码与 Task 8 的测试都按同序使用。`ImportPlaylistFileAsync(string? presetPath = null) → Task<PlaylistImportReport?>` 与 `ExportPlaylistFileAsync() → Task<string?>` 的签名在 Task 6/7 定义，Task 8 的 `Func<string?, Task<PlaylistImportReport?>>` 参数类型与 Task 9/10 的方法组转换一致（`string?` 对 `string?`，不会触发 CS8622 可空性警告）。`FilterPlaylistPaths` / `PlaylistFileFormats.Extensions` / `Icon.Import` / `Icon.Export` / `ConfirmDialog.ShowInfo` / `PlaylistImportReportFormatter.DialogTitle` 均先定义（Task 2/8）后使用（Task 9/10）。
