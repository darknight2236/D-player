using UmaPlayer.Models;

namespace UmaPlayer.Services;

public sealed class StubAudioDeviceManager : IAudioDeviceManager
{
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() => Array.Empty<AudioDeviceInfo>();
    public AudioDeviceInfo? CurrentDevice => null;
    public void SelectDevice(string deviceId) { }
}
