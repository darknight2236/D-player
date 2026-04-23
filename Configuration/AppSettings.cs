namespace UmaPlayer.Configuration;

public sealed record AppSettings
{
    public float DefaultVolume { get; init; } = 0.8f;
    public string OutputMode { get; init; } = "WasapiShared";
    public string? PreferredDeviceId { get; init; }
    public string? LastPlayedPath { get; init; }
    public double WindowLeft { get; init; }
    public double WindowTop { get; init; }
    public double WindowWidth { get; init; } = 800;
    public double WindowHeight { get; init; } = 450;
}
