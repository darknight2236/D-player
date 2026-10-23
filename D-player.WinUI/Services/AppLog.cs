using System.Globalization;
using System.IO;
using System.Text;

namespace DPlayer.WinUI.Services;

/// <summary>
/// 最小本地日志：追加到 app.log，超过 1 MiB 轮转到 app.log.1。
/// 任何 IO 失败都静默吞掉——日志不能成为新的崩溃源。
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "app.log");
        }
        catch
        {
            _path = null;
        }
    }

    public static void Write(string message, Exception? ex = null)
    {
        var path = _path;
        if (path is null) return;

        try
        {
            lock (Gate)
            {
                RotateIfNeeded(path);
                var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} " +
                           $"[T{Environment.CurrentManagedThreadId}] {message}" +
                           (ex is null ? string.Empty : Environment.NewLine + ex);
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不改变应用行为
        }
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < MaxBytes) return;

        var backup = path + ".1";
        if (File.Exists(backup)) File.Delete(backup);
        File.Move(path, backup);
    }
}
