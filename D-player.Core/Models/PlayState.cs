namespace DPlayer.Models;

/// <summary>
/// 播放器三态状态机：停止 / 播放中 / 暂停。
/// VM 通过 PlayStateToIconConverter 转换为 ▶/⏸ 图标。
/// </summary>
public enum PlayState { Stopped, Playing, Paused }
