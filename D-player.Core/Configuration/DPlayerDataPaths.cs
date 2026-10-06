using System.IO;

namespace DPlayer.Configuration;

/// <summary>
/// 用户数据目录（settings.json / queue.json / library-cache.json 的落点）。
/// 文件夹名由 UI 壳注入：WPF 壳 "D-player"，WinUI 壳 "D-player-winui"——
/// 两个壳各写各的目录，互不覆盖（无跨进程锁，共用目录会互相踩）。
/// </summary>
public sealed record DPlayerDataPaths
{
    /// <summary>无参构造 —— 配合对象初始化器使用，如 <c>new DPlayerDataPaths { FolderName = "D-player" }</c>。</summary>
    public DPlayerDataPaths()
    {
    }

    /// <summary>位置式构造 —— <c>new DPlayerDataPaths(root, folderName)</c>，两个参数都给全时使用。</summary>
    public DPlayerDataPaths(string Root, string FolderName)
    {
        this.Root = Root;
        this.FolderName = FolderName;
    }

    public string Root { get; init; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    public string FolderName { get; init; } = string.Empty;

    /// <summary>数据目录绝对路径；FolderName 为空时即 Root（测试用）。</summary>
    public string Directory => string.IsNullOrEmpty(FolderName) ? Root : Path.Combine(Root, FolderName);
}
