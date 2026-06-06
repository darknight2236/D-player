namespace UmaPlayer.Models;

/// <summary>
/// 音频输出设备的元信息 —— 当前为预留模型，配合未来的多设备切换 UI。
/// 目前 StubAudioDeviceManager 返回空集合，未真正枚举系统设备。
/// </summary>
/// <param name="Id">设备 ID（一般为 MMDevice.ID）。</param>
/// <param name="Name">用户可读的设备名称。</param>
/// <param name="IsDefault">是否为系统默认输出设备。</param>
public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);
