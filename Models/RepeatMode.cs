namespace UmaPlayer.Models;

/// <summary>
/// 循环播放模式：
///   Off  —— 不循环，列表播完即停
///   List —— 列表循环，最后一首播完跳回第一首
///   One  —— 单曲循环，当前曲反复播（仅自动触发生效；用户手动 Next 仍跳走）
/// </summary>
public enum RepeatMode { Off, List, One }
