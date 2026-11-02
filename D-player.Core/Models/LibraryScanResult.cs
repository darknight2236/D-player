namespace DPlayer.Models;

/// <summary>
/// 文件夹扫描结果(库根目录扫描)。
/// Files: 音频文件绝对路径。
/// IsUnavailable: 根目录无法枚举(路径为空/不存在/无权限/IO 失败)。
/// 该标记把“根目录暂时不可用”与“根目录存在但为空”区分开,
/// 避免把一次失败的枚举当成“所有文件已删除”而覆盖掉缓存。
/// </summary>
public sealed record LibraryScanResult(IReadOnlyList<string> Files, bool IsUnavailable);
