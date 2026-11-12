using System.Collections.Generic;
using System.Text.Json.Serialization;
using DPlayer.Configuration;
using DPlayer.Models;

namespace DPlayer.Services;

/// <summary>
/// 三个 Json 持久化服务(队列 / 库缓存 / 设置)的源生成元数据(Phase 29)。
/// 只替换元数据来源, 转换器与写入选项仍由各服务的 <c>JsonSerializerOptions</c> 决定;
/// 契约由 <c>JsonContractParityTests</c> 对照反射基线锁定。
/// </summary>
[JsonSerializable(typeof(QueueState))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(Dictionary<string, List<LibraryCacheEntry>>))]
[JsonSerializable(typeof(QueueStateV1))]
[JsonSerializable(typeof(RepeatMode))]
internal partial class DPlayerJsonContext : JsonSerializerContext;
