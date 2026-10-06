using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 占位实现 —— 始终返回空设备列表。
/// 后续可替换为基于 NAudio.CoreAudioApi.MMDeviceEnumerator 的真实实现。
/// </summary>
public sealed class StubAudioDeviceManager : IAudioDeviceManager
{
    public IReadOnlyList<AudioDeviceInfo> EnumerateDevices() => Array.Empty<AudioDeviceInfo>();
    public AudioDeviceInfo? CurrentDevice => null;
    public void SelectDevice(string deviceId) { }
}
