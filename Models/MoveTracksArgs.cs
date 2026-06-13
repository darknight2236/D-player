namespace UmaPlayer.Models;

/// <summary>
/// 队列内拖拽重排命令的参数（Phase 5）。
///
/// 由 View 层（PlaylistView.xaml.cs）的 Drop handler 构造，传给
/// PlaylistViewModel.MoveTracksCommand。
///
/// 不变量（由 View 层保证）：
///   - SourceIndices 升序无重复
///   - 每项 ∈ [0, Queue.Count)
///   - TargetIndex ∈ [0, Queue.Count]，i 表示插到 i 之前；Count 表示末尾
///
/// 用 record 与 AppSettings / QueueState 风格保持一致。
/// </summary>
public sealed record MoveTracksArgs(
    IReadOnlyList<int> SourceIndices,
    int TargetIndex);
