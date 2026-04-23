namespace UmaPlayer.Models;

public sealed record Track(
    string FilePath,
    string Title,
    string? Artist,
    string? Album,
    TimeSpan Duration);
