using System.IO;

namespace DPlayer.WinUI.Services;

internal static class WinUiAppPaths
{
    public const string DataFolderName = "D-player-winui";

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFolderName);

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
}
