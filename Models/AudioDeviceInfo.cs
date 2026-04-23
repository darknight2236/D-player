namespace UmaPlayer.Models;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);
