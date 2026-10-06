using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 音频输出设备管理抽象（预留）—— 未来支持 UI 中切换扬声器/耳机/虚拟声卡。
/// 当前由 StubAudioDeviceManager 占位，未实现真正的 MMDevice 枚举。
/// </summary>
public interface IAudioDeviceManager
{
    IReadOnlyList<AudioDeviceInfo> EnumerateDevices();
    AudioDeviceInfo? CurrentDevice { get; }
    void SelectDevice(string deviceId);
}
