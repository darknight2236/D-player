# 文档地图

## 当前文档

- [README](../README.md) — 入口、快速开始、当前限制
- [PROJECT.md](./PROJECT.md) — 架构、模块、数据流、已知陷阱
- [COUPLING.md](./COUPLING.md) — 耦合分析、隐式契约、不要做的事
- [PHASE25-ACCEPTANCE.md](./PHASE25-ACCEPTANCE.md) — 当前最近一次验收（搜索与实时监听）
- [PHASE24-ACCEPTANCE.md](./PHASE24-ACCEPTANCE.md) — 音乐库视图验收

## 历史验收与决策

- [PHASE23-ACCEPTANCE.md](./PHASE23-ACCEPTANCE.md) — WinUI 频谱/EQ（已验收）
- [PHASE22-ACCEPTANCE.md](./PHASE22-ACCEPTANCE.md) — WinUI 功能补齐
- [PHASE21-ACCEPTANCE.md](./PHASE21-ACCEPTANCE.md) — WinUI 视觉打磨
- [PHASE20-COMPARISON.md](./PHASE20-COMPARISON.md) — 两壳对比与续投决策

这些文档保留当时读数，不回填当前状态；当前能力以 README/PROJECT 为准。

## 设计稿

- `superpowers/specs/` — Phase 14–25 的 D-player 设计稿；Phase 24/25 是当前音乐库实现的直接依据。
- `superpowers/plans/` — 只保留两份刻意未跟踪的 Phase 24/25 本地计划，不属于发布面。
- `archive/specs/` — UmaPlayer 时代（Phase 1–13）设计稿。
- `archive/plans/` — Phase 1–23 实现计划。

## 维护规则

- 现状文档（README/PROJECT/COUPLING）在行为变化时更新；验收文档是历史证据，不回改。
- 不再把提交清单、逐阶段计数历史复制进现状文档；查历史用 `git log`。
- 新增阶段性设计/计划时，完成后移入 `archive/` 或删除（git 历史仍保留）。
