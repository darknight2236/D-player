using UmaPlayer.Models;

namespace UmaPlayer.Services;

public interface IAudioDeviceManager
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? CurrentDevice { get; }
    void SelectDevice(string deviceId);
}
