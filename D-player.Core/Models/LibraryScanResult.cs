namespace DPlayer.Models;

/// <summary>
/// 文件夹扫描结果(库根目录扫描)的封闭层级。
/// 两个分支互斥, 由类型本身保证:
/// <see cref="ScanAvailable"/>: 根目录枚举成功, Files 为音频文件绝对路径(可以为空)。
/// <see cref="ScanUnavailable"/>: 根目录无法枚举(路径为空/不存在/无权限/IO 失败)。
/// 该区分把“根目录暂时不可用”与“根目录存在但为空”分开,
/// 避免把一次失败的枚举当成“所有文件已删除”而覆盖掉缓存。
/// </summary>
public closed record class LibraryScanResult;

/// <summary>根目录枚举成功; Files 为空表示目录存在但没有音频文件。</summary>
public sealed record class ScanAvailable(IReadOnlyList<string> Files)
    : LibraryScanResult;

/// <summary>根目录无法枚举; 调用方不得据此覆盖该根的缓存。</summary>
public sealed record class ScanUnavailable : LibraryScanResult
{
    public static readonly ScanUnavailable Instance = new();
}
