using UmaPlayer.Models;

namespace UmaPlayer.Services;

public interface IPlaybackService : IDisposable
{
    PlayState State { get; }
    Track? CurrentTrack { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    float Volume { get; set; }

    Task LoadAsync(Track track);
    void Play();
    void Pause();
    void Stop();
    void Seek(TimeSpan position);

    event Action<PlayState> StateChanged;
    event Action<TimeSpan> PositionChanged;
    event Action<TimeSpan> DurationChanged;
    event Action<string>? PlaybackError;
}
