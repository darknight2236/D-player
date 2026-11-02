using System.IO;

namespace DPlayer.Services;

/// <summary>库根目录的规范化：绝对路径、去尾部分隔符、大小写不敏感去重。</summary>
public static class LibraryPathNormalizer
{
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string full;
        try { full = Path.GetFullPath(path.Trim()); }
        catch { return null; }

        var root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return full.Length == 0 ? null : full;
    }

    public static IReadOnlyList<string> NormalizeMany(IEnumerable<string>? paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths ?? Array.Empty<string>())
        {
            var normalized = Normalize(path);
            if (normalized is not null && seen.Add(normalized))
                result.Add(normalized);
        }
        return result;
    }
}
