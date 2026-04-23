using NAudio.Wave;

namespace UmaPlayer.Services;

public interface IAudioOutputFactory
{
    IWavePlayer CreateOutput();
}
