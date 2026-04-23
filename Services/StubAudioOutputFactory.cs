using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace UmaPlayer.Services;

public sealed class StubAudioOutputFactory : IAudioOutputFactory
{
    public IWavePlayer CreateOutput() => new WasapiOut(AudioClientShareMode.Shared, 100);
}
