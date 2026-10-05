# Phase 18：播放列表文件导入导出（M3U / M3U8 / PLS）设计

> 设计日期：2026-10-05 · 对应分支：`master` · 状态：**已实现**
>
> 目标：让歌单能与外部世界互通——**导入** `.m3u` / `.m3u8` / `.pls` 建歌单或追加到当前歌单，**导出**当前歌单为 `.m3u8`（绝对路径 + 完整 `#EXTINF`）。导入侧严格过滤不可用条目并给出结果报告；编码支持 UTF-8 / UTF-16(BOM) / GBK 回退，保证中文路径不乱码。

---

## 0. 背景与动机

当前歌单只能通过三种方式填充：文件对话框选音频（`PlaylistViewModel.AddToQueue`）、导入文件夹（`PlaylistsViewModel.ImportFolderAsync` 建文件夹绑定歌单 / `PlaylistViewModel.ImportFolderToCurrent` 追加）、以及 OS 拖拽音频文件。三条路都要求音频**已经在本机某个目录里按用户期望的组织方式摆好**。

现实缺口有两处：

1. **进来**：用户从旧播放器、论坛、别人分享的压缩包拿到的 `.m3u` / `.pls` 现在完全无法使用——拖进窗口会被 `DragDropExtensions.FilterAudioPaths` 的白名单静默剔除，用户看不到任何反馈。
2. **出去**：歌单只存在于 `%LocalAppData%\D-player\playlists.json`（自有 schema v3），无法交给车机、其他播放器或备份脚本。

`Playlist` 记录里 `Items` 就是绝对路径字符串列表，`Track` 有标题/艺术家/时长——**两种格式所需的字段项目里已经全都有**，缺的只是格式编解码层与两个入口。

## 1. 目标与范围

### Goals

- **导入**：解析 `.m3u` / `.m3u8` / `.pls`，条目解析为绝对路径，过滤后批量读元数据入列。
- **导出**：当前查看的歌单写出为 `.m3u8`（extended M3U，UTF-8 无 BOM，绝对路径）。
- **双入口分层**（沿用现有约定）：容器级（侧边栏）= 新建歌单；歌单级（工具栏）= 追加当前歌单。
- **拖拽导入**：拖到侧边栏 → 新建歌单；拖到歌单列表区 → 追加当前歌单。
- **严格过滤 + 结果报告**：文件缺失、后缀不在白名单、网络流条目一律跳过并计数，导入后由 View 弹主题化信息框汇总。
- **编码鲁棒**：UTF-8（含 BOM）、UTF-16LE/BE（BOM）、GBK 回退，中文路径不乱码。
- 服务层可纯单元测试（临时目录造文件），VM 层用 NSubstitute mock。

### Non-Goals

- **不导出 PLS**：M3U8 是所有播放器的最大公约数，PLS 写出属低频需求（砍掉一个 writer 与其测试矩阵）。
- **不支持网络流播放**（`http://` / `https://` 条目）：只识别并计入"格式不支持"，不新增流媒体解码链路。
- **不用列表文件里的标题/时长覆盖音频文件的真实元数据**：`#EXTINF` 与 PLS 的 `Title=` / `Length=` 一律忽略。
- **不做"导出全部歌单"批量**、不做导出选项对话框（绝对/相对、是否带 `#EXTINF` 都不给用户选）。
- **不做相对路径导出**：库里存的就是绝对路径，写相对路径需要额外的根目录归一化且部分播放器解析不佳。
- 不做列表内条目去重（重复行原样保留）。
- 不改播放链路、不改持久化 schema（导入的歌单就是普通歌单，`SourceFolder = null`，走既有 schema v3 落盘）。

## 2. 决策记录（brainstorming 结论）

| # | 议题 | 决定 | 理由 |
|---|------|------|------|
| 1 | 范围与格式 | 双向；导入 M3U/M3U8/PLS，导出只写 M3U8 | M3U8 通用性最高，省掉 PLS writer 与其往返测试 |
| 2 | 导入落地 | 双入口分层：侧边栏新建歌单 / 工具栏追加当前 | 与 `ImportFolderAsync`（容器级建歌单）vs `AddToQueue`（歌单级追加）已有约定对称，用户已熟悉 |
| 3 | 不完美条目 | 严格过滤 + 结果报告 | 与拖拽/导入文件夹的白名单过滤语义一致；队列里不出现点不动的死条目；报告消除"200 首只进来 180 首"的困惑 |
| 4 | 编码 | UTF-8 严格探测 + GBK 回退，接受新增官方包 `System.Text.Encoding.CodePages` | .NET 里 `Encoding.GetEncoding(936)` 必须先注册 CodePages provider，否则抛异常；纯 UTF-8 会让 GBK 中文路径整表失效 |
| 5 | 导出形状 | 绝对路径 + 完整 `#EXTINF` | 项目已有标题/艺术家/时长，零额外成本；绝对路径对本地播放器与车机最稳 |
| 6 | 拖拽 | 支持，按拖放区域分语义 | 与双入口分层一致，且直接复用同一条"解析→过滤→入列→报告"管道 |
| 7 | 架构形状 | 新增门面服务 `IPlaylistFileService`（方案 A） | 两个 VM 共用同一条管道，过滤与计数必须集中；服务可纯单测；与 `JsonPlaylistService` / `JsonLibraryCache` 风格一致 |
| 8 | CodePages 注册点（实现期修订） | 从 `App.OnStartup` 改为 `PlaylistFileEncoding` **静态构造函数**；`D-player.csproj` 不加包引用 | `Encoding.GetEncoding(936)` 只出现在该类内部，静态构造函数把"注册早于解码"从启动顺序契约变成类型不变量；且 `System.Text.Encoding.CodePages` 在 net10.0 框架隐含，显式 `PackageReference` 触发 NU1510 警告、破坏 0 警告门禁 |

被否掉的方案：**B. 静态 helper 不进 DI**（过滤与报告计数在两个 VM 重复，且无法 mock，VM 测试必须真碰磁盘）；**C. 扩展 `ILibraryScannerService`**（把"文件夹→音频文件"与"列表文件→条目"两种职责混进一个已在 COUPLING 登记契约的类型，放大风险面）。

## 3. 架构与文件清单

```
Services/PlaylistFiles/                 ← 新增目录
  PlaylistFileFormats.cs                静态：后缀白名单 + IsPlaylistFile + 对话框过滤器字符串
  IPlaylistFileService.cs               门面接口
  PlaylistFileService.cs                门面实现（编排 编码探测 → 解析 → 归一化 → 过滤计数）
  PlaylistImportResult.cs               服务层结果 record
  PlaylistFileEncoding.cs               内部：字节 → 字符串（BOM 判定 + 严格 UTF-8 试解码 + GBK 回退）
  M3uParser.cs                          内部：非 # 行 → 原始条目
  PlsParser.cs                          内部：File<N>= → 原始条目
  M3u8Writer.cs                         内部：Track 列表 → extended M3U8 文本
ViewModels/PlaylistImportReport.cs      ← 新增：VM 层报告 record（View 拼文案用）
```

改动的现有文件：

| 文件 | 改动 |
|------|------|
| `D-player.csproj` | **未改动**（实现期修订，见决策 #8）：`System.Text.Encoding.CodePages` 在 net10.0 框架隐含，显式 `PackageReference` 触发 NU1510 |
| `App.xaml.cs` | **未改动**（实现期修订，见决策 #8）：provider 改在 `PlaylistFileEncoding` 静态构造函数注册 |
| `Services/IFileDialogService.cs` | 加 `SaveFile(filter, defaultFileName, defaultExtension)` |
| `Services/Win32FileDialogService.cs` | 实现 `SaveFile`（`Microsoft.Win32.SaveFileDialog`） |
| `Extensions/ServiceCollectionExtensions.cs` | 注册 `IPlaylistFileService`（Singleton，无状态） |
| `ViewModels/PlaylistsViewModel.cs` | ctor 加依赖；新增 `ImportPlaylistFileAsync`；`NullFileDialogService.SaveFile` + 新增 `NullPlaylistFileService`；`internal` 测试 ctor 跟着改 |
| `ViewModels/PlaylistViewModel.cs` | ctor 加依赖；新增 `ImportPlaylistFileAsync` / `ExportPlaylistFileAsync` |
| `Views/Dialogs/ConfirmDialog.xaml.cs` | 加 `ShowInfo`（单按钮，语义为信息而非错误） |
| `Views/Controls/DragDropExtensions.cs` | 加 `PlaylistFileExtensions` + `FilterPlaylistPaths` |
| `Views/Controls/PlaylistsSidebarView.xaml(.cs)` | 第三个 28×28 图标按钮；`DragOver`/`Drop` 加 FileDrop 分支 |
| `Views/Controls/PlaylistView.xaml(.cs)` | 工具栏加"导入列表"/"导出列表"；`DragOver`/`Drop` 分流播放列表文件 |
| `Themes/Icons.xaml` | 加 `Icon.Import` / `Icon.Export`（24×24 描边型 Geometry） |

## 4. 服务层契约

```csharp
public static class PlaylistFileFormats
{
    public static readonly IReadOnlyList<string> Extensions;   // ".m3u" ".m3u8" ".pls"
    public static bool IsPlaylistFile(string? path);           // 后缀判定，大小写不敏感；null/空/无后缀 → false
    public const string OpenFilter  = "播放列表|*.m3u;*.m3u8;*.pls|所有文件|*.*";
    public const string SaveFilter  = "M3U8 播放列表|*.m3u8";
}

public sealed record PlaylistImportResult(
    string SuggestedName,                    // 文件名去后缀；空则 "导入的歌单"
    IReadOnlyList<string> AcceptedPaths,     // 已归一化为绝对路径 + 存在 + 后缀在白名单
    int TotalEntries,                        // 列表文件里的原始条目数
    int SkippedMissing,                      // 解析出的路径 File.Exists 为假
    int SkippedUnsupported);                 // 后缀不在 AudioConstants.AudioExtensions，或为 URL 条目

public interface IPlaylistFileService
{
    Task<PlaylistImportResult> ImportAsync(string playlistFilePath);
    Task ExportAsync(string destPath, IReadOnlyList<Track> tracks);
}
```

**错误策略刻意不对称**，沿用项目既有约定：

- `ImportAsync` **绝不抛**（读侧对齐 `JsonPlaylistService.LoadAsync`）：列表文件不存在、无读权限、编码解码彻底失败、后缀不认识 → 返回 `SuggestedName` 取文件名、`AcceptedPaths` 为空、计数为 0 的结果。
- `ExportAsync` **可抛** `IOException` / `UnauthorizedAccessException`（写侧对齐设置/EQ 对话框的 `try/catch` + 错误框），由 VM 捕获后转成错误文案交给 View。

`IsPlaylistFile` 是静态而非实例方法：View 层拖拽判定（`DragDropExtensions`）不该为了一个后缀判断去 DI 取服务。

## 5. 解析语义

### 5.1 取什么

- **M3U / M3U8**：逐行；忽略空行与所有 `#` 开头的行（`#EXTM3U` / `#EXTINF` / 注释同等对待）；其余行 trim 后即为路径条目。
- **PLS**：INI 风格，只取 `[playlist]` 段内 key 形如 `File<N>` 的值（key 大小写不敏感，`<N>` 任意数字，**按出现顺序而非序号排序**——现实中不少工具序号不连续）；`Title<N>` / `Length<N>` / `NumberOfEntries` / `Version` 全部忽略；无 `=` 的行、其他 key、段头一律跳过。
- **刻意不提取 `#EXTINF` / `Title=` / `Length=`**：标题与时长只信 ATL 从音频文件读到的结果。省掉一整套"两个真相来源谁优先"的规则，也避免列表文件里的过时/错误信息污染队列。

### 5.2 路径归一化

以列表文件所在目录为 base：

```
entry 是 rooted（Path.IsPathRooted）→ Path.GetFullPath(entry)
否则                               → Path.GetFullPath(Path.Combine(baseDir, entry))
```

`GetFullPath` 同时负责消掉 `..\` 与重复分隔符。归一化后再做存在性与后缀判定。

### 5.3 编码探测

```
读全部字节
  ├─ EF BB BF        → UTF-8，跳过 BOM
  ├─ FF FE           → UTF-16LE，跳过 BOM
  ├─ FE FF           → UTF-16BE，跳过 BOM
  └─ 无 BOM → 用 UTF8Encoding(emitBOM:false, throwOnInvalidBytes:true) 试解码
                ├─ 成功 → UTF-8
                └─ DecoderFallbackException → Encoding.GetEncoding(936)（GBK）
```

GBK 依赖 `CodePagesEncodingProvider`，必须在解码前注册（见 §8）。UTF-16 的 BOM 检测成本是三行代码，PLS 偶见，留着当保险。

### 5.4 过滤与计数

对每个归一化后的条目：

| 条件 | 处理 |
|------|------|
| 是 URL（`http://` / `https://` / `mms://` / `rtsp://` 前缀，大小写不敏感） | `SkippedUnsupported++` |
| 后缀不在 `AudioConstants.AudioExtensions`（.mp3/.wma/.flac/.aac/.wav） | `SkippedUnsupported++` |
| `File.Exists` 为假 | `SkippedMissing++` |
| 以上都通过 | 加入 `AcceptedPaths` |

顺序是 URL → 后缀 → 存在性，保证一个条目只被计入一个桶。**不去重**：列表里的重复行原样保留（重复常是用户刻意排的循环）。

`AcceptedPaths` 为空时，VM 不建歌单、不动队列，只弹报告。

## 6. 导出语义

### 6.1 SaveFile 对话框

```csharp
string? SaveFile(string filter, string defaultFileName, string defaultExtension);   // 取消 → null
```

Win32 实现用 `Microsoft.Win32.SaveFileDialog`（`OverwritePrompt` 默认 true，覆盖询问交给系统对话框，不自己实现）。STA 约束与 `OpenFiles` 相同。`NullFileDialogService.SaveFile` 返回 `null`。

### 6.2 M3U8 文本

- 编码 UTF-8 **无 BOM**，行尾 `\r\n`（Windows 播放器与记事本都友好）。
- 首行 `#EXTM3U`。
- 每条两行：
  ```
  #EXTINF:{秒},{Artist - Title}
  {绝对路径}
  ```
  - `{秒}` = `(int)Math.Round(Duration.TotalSeconds)`；`Duration` 为零/未知 → `-1`。
  - `Artist` 为空 → 只写 `Title`（不留悬空的 `" - "`）；`Title` 也为空 → 写文件名。
  - 标题里的 `,` 无需转义（`#EXTINF` 只按第一个逗号切分）。
- 路径行原样写 `Track.FilePath`（库里存的就是绝对路径）。
- 默认文件名 `{歌单名}.m3u8`，`Path.GetInvalidFileNameChars()` 命中的字符替换为 `_`。

## 7. VM 接入

### 7.1 报告 record

```csharp
public sealed record PlaylistImportReport(
    string SourceFile,           // 列表文件名（多文件拖入时逐个累积）
    string? PlaylistName,        // 新建时=新歌单名；追加时=目标歌单名；全跳过时=null
    bool CreatedNewPlaylist,
    int Imported, int SkippedMissing, int SkippedUnsupported, int TotalEntries);
```

### 7.2 方法而非命令

VM 暴露**可 await 的公开方法**，View 在 `async void` 的 Click / Drop 处理器里 await 后拼文案弹框。选它而不是 `RelayCommand` + 事件订阅，理由：View 需要在弹框前后控制流程（`ClearButton_Click` 已是这个习惯），且方法返回值可以直接在单元测试里断言，不必去捕获事件。

```csharp
// PlaylistsViewModel —— 容器级 = 新建歌单
public Task<PlaylistImportReport?> ImportPlaylistFileAsync(string? presetPath = null);

// PlaylistViewModel —— 歌单级 = 追加当前 + 导出
public Task<PlaylistImportReport?> ImportPlaylistFileAsync(string? presetPath = null);
public Task<string?> ExportPlaylistFileAsync();   // null = 成功或用户取消；非 null = 错误文案
```

返回 `null` 表示用户取消了文件对话框，View 一个框都不弹。`presetPath` 非空时跳过对话框——这就是拖拽入口与对话框入口共用同一条管道的接点。

### 7.3 新建路径（PlaylistsViewModel）

`ImportPlaylistFileAsync` 骨架复用 `ImportFolderAsync`：

1. `presetPath ?? _fileDialog.OpenFiles(PlaylistFileFormats.OpenFilter)`，取消 → `null`。
2. `await _playlistFiles.ImportAsync(path)`。
3. `AcceptedPaths.Count == 0` → 返回 `Imported = 0` 的报告，**不建歌单**。
4. `await _scanner.ReadMetadataBatchAsync(AcceptedPaths)`（批量，与导入文件夹一致；单个失败静默跳过）。
5. 造 `Playlist` seed：`Id = Guid.NewGuid()`、`Name = SuggestedName`、`Items = 路径`、`CurrentIndex = -1`、`ShuffleEnabled = false`、`RepeatMode = Off`、**`SourceFolder = null`**。
6. `_factory(seed)` → 填 `Queue` → `HookPlaylistVm` → `Playlists.Add` → `ViewedPlaylist = vm`。
7. 返回报告（`CreatedNewPlaylist = true`）。

`SourceFolder = null` 是关键：导入的歌单是**普通歌单**，不写 library cache，重启时 `Hydrate` 走 `LoadMetadataForNormalPlaylistSync` 读文件元数据。第 6 步的集合变更会触发既有 `OnPlaylistsCollectionChanged` → `StateChanged` → MainViewModel debounce 存盘，不需要额外接线。

### 7.4 追加路径（PlaylistViewModel）

1. 同样取路径（对话框或 `presetPath`）→ `_playlistFiles.ImportAsync`。
2. `AcceptedPaths.Count == 0` → 返回 `Imported = 0` 的报告，队列不动。
3. 把 `AcceptedPaths` 直接喂给**既有的 `DropExternalFilesCommand`**（`PlaylistViewModel.DropExternalFiles(paths)`，逐个 `_metadataReader.ReadAsync` 后 `Queue.Add`，不自动播放）。不给 `PlaylistViewModel` 加 `ILibraryScannerService` 依赖。
4. 返回报告（`CreatedNewPlaylist = false`，`PlaylistName = Name`）。

`DropExternalFiles` 现有的 View 层契约是"传入 paths 已过滤，本命令不二次过滤"——导入路径天然满足：过滤已经在服务层做完，`AcceptedPaths` 就是过滤结果。这条契约从"只有拖拽入口满足"变成"拖拽与导入两个入口都满足"，需要在 COUPLING §5 里更新措辞。

入队会触发既有的 `Queue.CollectionChanged` → 容器 `StateChanged` → 存盘；不自动播放，与 `AddToQueue` 一致。

### 7.5 导出路径（PlaylistViewModel）

```
Queue.Count == 0 → View 层直接 return（不弹框）
dest = _fileDialog.SaveFile(SaveFilter, "{清洗后的歌单名}", ".m3u8")
dest is null → return null（用户取消）
try { await _playlistFiles.ExportAsync(dest, Queue.ToArray()); return null; }
catch (Exception ex) { return $"导出失败：{ex.Message}"; }
```

导出内容 = 当前队列顺序（不是 shuffle 后的播放顺序）。成功不弹提示，只有失败才由 View 走 `ShowError`。

## 8. 依赖与启动

> **实现期修订（决策记录 #8）**：本节原方案为「csproj 加包引用 + `App.OnStartup` 最早期注册」，落地时改为下述口径，设计稿以此为准。

- **不加 NuGet 包引用**：`System.Text.Encoding.CodePages` 在 net10.0 上框架隐含（framework-implicit），`CodePagesEncodingProvider` 开箱可用；显式 `PackageReference` 会被 SDK 判定冗余并触发 NU1510 警告，破坏项目 0 警告门禁。`D-player.csproj` 未改动。
- **注册点在 `PlaylistFileEncoding` 的静态构造函数**：
  ```csharp
  static PlaylistFileEncoding()
  {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
  }
  ```
  `Encoding.GetEncoding(936)` 只出现在该类内部，静态构造函数保证注册永远早于本类任何解码调用——"注册先于解码"由类型自身保证，不依赖 App 启动顺序。`App.xaml.cs` 未改动。重复注册无害（`RegisterProvider` 幂等；测试进程为造 GBK 字节自行注册一次也不冲突）。
- **约束**：不要把 GBK 解码搬到 `PlaylistFileEncoding` 之外的类型——搬走就等于把注册时机重新变成一条口头约定（登记于 COUPLING §5）。

## 9. UI 入口与拖拽

### 9.1 按钮

- **侧边栏**：`+ / −` 之后加第三个 28×28 图标按钮，`Icon.Import`，ToolTip「导入播放列表」，`Click="ImportBtn_Click"`。
- **歌单工具栏**：左侧组顺序改为 **添加 / 导入文件夹 / 导入列表 / 导出列表 / 清空**（清空是危险操作，保持最右）。两个新按钮用与"导入文件夹"相同的 `Path + TextBlock` 结构与 `Padding="12,4"` `Margin="8,0,0,0"`。
- **导出只放工具栏、不放侧边栏**：侧边栏按钮作用于"选中项"，而导出语义是"当前查看的歌单"，`ViewedPlaylist` 与选中项在键盘导航下可能不同步，放侧边栏会产生歧义。
- 新增图标 `Icon.Import` / `Icon.Export`（24×24 viewbox 描边型 Geometry，与 `Themes/Icons.xaml` 现有约定一致；现无合适图标可复用）。

### 9.2 拖拽

- `DragDropExtensions` 加 `PlaylistFileExtensions`（proxy 到 `PlaylistFileFormats.Extensions`，维持"单一来源、避免漂移"的既有约定）与对称的 `FilterPlaylistPaths(paths)`。
- **侧边栏**：`PlaylistList_DragOver` 现在的 `else → Effects = None` 分支改为——`FileDrop` 且 `FilterPlaylistPaths` 非空 → `Effects = Copy`（**不显示重排插入线**，这不是重排）；否则仍 `None`。`PlaylistList_Drop` 加 FileDrop 分支：逐个 `await _vm.ImportPlaylistFileAsync(path)`，聚合报告。
- **歌单列表区**：`QueueList_DragOver` / `Root_DragOver` 的 Copy 判定从"音频非空"改为"音频非空 **或** 播放列表文件非空"；Drop 时把两组分别送入既有音频入队管道与 `ImportPlaylistFileAsync`。混合拖入（音频 + m3u）两条都跑。
- 内部重排格式（`QueueItemsFormat` / `PlaylistItemsFormat`）优先级不变，外部 FileDrop 分支放在其后。

### 9.3 报告文案（View 组装，走 `ConfirmDialog.ShowInfo`）

```
单文件·新建：
  已导入「Rock.m3u8」→ 新歌单「Rock」
  18 首入列，跳过 2 条（文件缺失 1 / 格式不支持 1）

单文件·追加：
  已导入「Rock.m3u8」→ 歌单「我的歌单」
  18 首入列，跳过 2 条（文件缺失 1 / 格式不支持 1）

全跳过：
  「Old.pls」没有可导入的条目
  跳过 5 条（文件缺失 3 / 格式不支持 2）

无跳过时第二行简化为「18 首入列」。

多文件（拖拽）：
  已导入 2 个播放列表
  · Rock.m3u8 → 新歌单「Rock」：18 首（跳过 2）
  · Old.pls → 没有可导入的条目（跳过 5）
```

`ShowInfo` 与 `ShowError` 共用 `ShowCore(showCancel: false)`，区别只在语义命名（导入报告不是错误）。

## 10. 测试计划

### 10.1 `Tests/Services/PlaylistFileServiceTests.cs`（新增，临时目录 fixture）

解析与归一化：

1. M3U 绝对路径条目原样返回。
2. M3U 相对路径按列表文件所在目录解析。
3. M3U 含 `..\` 的相对路径被 `GetFullPath` 消解。
4. `#EXTM3U` / `#EXTINF` / 任意 `#` 注释行与空行被忽略，不产生条目也不计数。
5. PLS `File1=` / `File2=` 正常提取；`NumberOfEntries` / `Version` / `Title<N>` / `Length<N>` 被忽略。
6. PLS 序号不连续或乱序（`File3` 在 `File1` 前）→ 按出现顺序返回。
7. PLS key 大小写混合（`file1=` / `FILE2=`）→ 仍能提取。
8. PLS 网络流条目（`http://`）→ 计入 `SkippedUnsupported`。

编码：

9. UTF-8 无 BOM 的中文路径正确解码。
10. UTF-8 带 BOM → BOM 不进入首个条目。
11. UTF-16LE 带 BOM → 正确解码。
12. GBK 编码的中文路径 → 走回退分支正确解码（验证 CodePages provider 已注册）。

过滤计数：

13. 路径不存在 → `SkippedMissing++`，不进 `AcceptedPaths`。
14. 后缀 `.ogg` / `.txt` → `SkippedUnsupported++`。
15. 后缀大小写混合（`.MP3`）→ 通过。
16. 空文件 / 只有注释 → `TotalEntries = 0`、`AcceptedPaths` 空、不抛。
17. 列表文件路径本身不存在 → 空结果、不抛。
18. `SuggestedName` = 文件名去后缀。

导出与往返：

19. 导出文本首行 `#EXTM3U`、每条 `#EXTINF:{秒},{Artist - Title}` + 路径行、`\r\n` 行尾、文件无 BOM。
20. `Artist` 为空 → `#EXTINF:{秒},{Title}`（无悬空 `" - "`）。
21. `Duration` 为零 → `#EXTINF:-1,...`。
22. 往返：导出 → 再导入，`AcceptedPaths` 与原队列路径序列一致（用空 `.mp3` 占位文件即可，服务层只判存在性不读元数据）。

### 10.2 VM 测试（扩展现有 `Tests/ViewModels/PlaylistsViewModelTests.cs` / `PlaylistViewModelTests.cs`）

- 新建路径：建出一个歌单、名字取 `SuggestedName`、`ViewedPlaylist` 指向它、`SourceFolder` 为 null、`StateChanged` 被触发。
- 新建路径·全跳过：`AcceptedPaths` 空 → 歌单数不变、报告 `Imported = 0`。
- 追加路径：条目按顺序进 `Queue`、`CurrentIndex` 不变、不自动播放。
- 追加路径·空结果：队列不动。
- 取消对话框：两个方法都返回 `null`，不动任何状态。
- `presetPath` 非空时不调用 `IFileDialogService`（`DidNotReceive()`）。
- 导出：调用 `SaveFile` 传入清洗后的默认文件名；`ExportAsync` 抛异常时返回错误文案；取消时返回 `null` 且不调用 `ExportAsync`。

### 10.3 现有测试桩补齐

`NullFileDialogService.SaveFile` → `null`；新增 `NullPlaylistFileService`（`ImportAsync` 返回空结果、`ExportAsync` no-op）；两个 VM 的 `internal` 测试 ctor 参数表跟着改。

## 11. 文档同步

- `docs/PROJECT.md`：新增 Phase 18 小节（服务层 + 双入口 + 拖拽双轨）、目录树补 `Services/PlaylistFiles/`。
- `docs/COUPLING.md` §5 新增隐式契约：
  1. **拖拽双轨白名单**：`FilterAudioPaths` 与 `FilterPlaylistPaths` 互不重叠，Drop 处理器必须两个都查，否则 `.m3u` 会被静默丢弃（回到 Phase 18 之前的行为）。
  2. **CodePages provider 注册点**：在 `PlaylistFileEncoding` 的静态构造函数里（实现期修订，见 §8 与决策 #8），`Encoding.GetEncoding(936)` 只出现在该类内部，注册永远早于解码；不要把 GBK 解码搬到别的类型里。
  3. **Import 不抛 / Export 抛**：读侧吞异常返回空结果，写侧异常必须冒到 VM 转错误文案；不要把 `ExportAsync` 包成不抛。
  4. **`#EXTINF` / `Title=` 刻意忽略**：标题时长只信音频文件；后续若想用列表文件的元数据，需要先定优先级规则。
  5. **导入报告文案由 View 组装**：VM 只返回结构化 `PlaylistImportReport`，不在 VM 里拼中文句子。
  6. **`SourceFolder = null` 是导入歌单的身份标记**：设成非 null 会让它被当作文件夹绑定歌单，触发 cache 读写与"刷新文件夹"按钮。
  7. **`DropExternalFilesCommand` 的"paths 已过滤"契约新增一个调用方**：原本只有 View 拖拽入口，Phase 18 起 VM 内部导入路径也会调用它；任何新增调用方必须自己保证路径已过滤。
  §7 don't-do 补两条：❌ 不要在 `PlaylistViewModel` 里为导入引入 `ILibraryScannerService`（追加路径复用既有外部文件入队）；❌ 不要把导出入口放到侧边栏（选中项 ≠ 查看项，语义歧义）。
- `README.md`：功能列表加"播放列表导入（M3U/M3U8/PLS）与导出（M3U8）"。
- 实现计划：`docs/superpowers/plans/2026-10-05-d-player-phase18-playlist-file-io-implementation.md`。

## 12. 验收标准

1. `dotnet build` 0 警告 0 错误；`dotnet test` 现有 96 个测试 + 新增测试全绿。
2. GUI 实地验证（computer-use）：
   - 侧边栏按钮导入 `.m3u8` → 新歌单出现在列表并成为当前查看项，曲目带正确标题/艺术家/时长。
   - 工具栏"导入列表"导入 `.m3u`（GBK 编码、含中文路径）→ 追加到当前歌单末尾，中文不乱码。
   - 导入 `.pls`（含网络流条目与缺失文件）→ 报告框显示正确的跳过计数。
   - 导入全部条目都失效的列表 → 不新建歌单，报告显示"没有可导入的条目"。
   - 拖 `.m3u8` 到侧边栏 → 新建歌单；拖到歌单列表区 → 追加当前歌单；混合拖（音频 + m3u）两条都生效。
   - "导出列表" → 保存对话框默认文件名为歌单名；导出文件用记事本核对格式；把导出文件再导入 → 队列与原歌单一致（往返）。
   - 导出时目标文件被占用/只读 → 弹主题化错误框而非系统 MessageBox。
   - 重启应用 → 导入的歌单仍在，元数据正常（走普通歌单加载路径，不是文件夹缓存路径）。
3. 全应用无 `MessageBox` 调用残留（延续 Phase 17 后的状态）。

## 13. 风险与边界

- **GBK 探测误判**：严格 UTF-8 解码成功即认定 UTF-8。极少数 GBK 字节序列恰好是合法 UTF-8 时会误判为 UTF-8 并产生乱码路径——后果是这些条目落入"文件缺失"计数，用户能从报告里看出来，不会静默错乱。可接受。
- **`SaveFileDialog` 覆盖已有文件**：交给系统对话框的 `OverwritePrompt` 询问，不自实现确认框。
- **大列表阻塞 UI**：`ImportAsync` 是文件读 + 纯字符串处理（几十 KB 级），在 UI 线程 await 可接受；真正的耗时在 `ReadMetadataBatchAsync` / `ReadAsync`，二者已是既有异步路径。上千条目的列表若实测卡顿，再考虑进度提示（本阶段不做）。
- **拖拽事件处理器变成 `async void`**：异常必须在处理器内 try/catch 兜住，否则会崩进程。`ImportPlaylistFileAsync` 本身不抛（读侧不抛 + VM 捕获写侧），但处理器仍要有 `try/catch` 兜底。
- **`FilterPlaylistPaths` 与内部重排格式的优先级**：Drop 里必须先查内部格式（`PlaylistItemsFormat` / `QueueItemsFormat`），再查 FileDrop，否则从别处拖来的歌单项可能被误判为文件导入。
- **混合拖入的报告合并**：音频入队路径现在没有报告，只有播放列表路径有；聚合报告只描述播放列表部分，音频部分沿用现状（静默入队）。

## 14. 参考

- 现有分层与入口约定：`ViewModels/PlaylistsViewModel.cs`（`ImportFolderAsync`）、`ViewModels/PlaylistViewModel.cs`（`AddToQueue` / `ImportFolderToCurrent` / 外部文件入队）。
- 白名单单一来源：`Models/AudioConstants.cs`、`Views/Controls/DragDropExtensions.cs`。
- 对话框与主题：`Services/IFileDialogService.cs`、`Services/Win32FileDialogService.cs`、`Views/Dialogs/ConfirmDialog.xaml.cs`、`Themes/Icons.xaml`、`Themes/Controls.xaml`。
- 持久化与加载路径：`Services/JsonPlaylistService.cs`、`Models/Playlist.cs`（`SourceFolder` 语义）、`PlaylistsViewModel.Hydrate`。
- 先例：Phase 10 库扫描与文件夹绑定歌单（`docs/superpowers/specs/2026-06-14-uma-player-phase10-library-scan-design.md`）、Phase 5 拖拽（`2026-06-12-uma-player-phase5-drag-drop-design.md`）。
- 格式规范：M3U/M3U8（`#EXTM3U` + `#EXTINF:时长,显示名`）、PLS（INI 风格 `[playlist]` + `File<N>` / `Title<N>` / `Length<N>` + `NumberOfEntries`）。
