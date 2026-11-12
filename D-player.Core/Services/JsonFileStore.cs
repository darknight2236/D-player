using System.IO;
using System.Text.Json;

namespace DPlayer.Services;

internal static class JsonFileStore
{
    internal static async Task<string> ReadAllTextAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    internal static async Task WriteAtomicAsync<T>(
        string path, T value, JsonSerializerOptions options)
    {
        var tmp = path + ".tmp";
        try
        {
            await using (var stream = File.Create(tmp))
                await JsonSerializer.SerializeAsync(stream, value, options)
                    .ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch { /* best-effort */ }
            throw;
        }
    }
}
