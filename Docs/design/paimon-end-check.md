# 战斗结束检测：派蒙辅助检测的语义修正

状态：已实施。

## 背景

自动战斗在结束判定前会按 L 打开编队界面，随后：

1. 用 `Bv.IsInMainUi`（左上四分之一 ROI 内模板匹配 `PaimonMenu`）判断派蒙头像是否可见；
2. 派蒙可见 → 认为按 L 未生效、编队界面没打开；
3. 原实现据此**立即** `return false` 并跳过后续的黄条判定（`IsYellow`/`IsWhite`）。

问题：第 2 步的推论只到"按 L 未生效"，原实现却把它当成"战斗未结束"的**不可推翻判决**。
一旦派蒙检测误判，战斗结束检查就会被无限次拦截，自动战斗永远不结束，日志反复输出
"派蒙头像可见，提前跳出战斗结束检查"。

触发误判的主要成因：

- **检测延时短于输入延迟**。默认 `PaimonEndCheckDelay = 0.075s`，`clamp` 下限 0.05s。
  而游戏内键鼠响应有 30~60ms 延迟、截图来源图本身也有延迟，很容易取到按 L **之前**的旧帧，
  于是"界面还没打开"被读成"派蒙可见"。
- **缺少收敛路径**。该分支只有 `return false`，没有任何次数上限或替代判定，
  一旦进入就每次都提前退出。
- 该分支还会按 `GIActions.Drop`（默认 X）取消界面，在战斗中反复触发下落键。

## 备选方案

1. **整体禁用派蒙检测**（把 `PaimonEndCheckEnabled` 默认改回 false）。
   最简单，但等于放弃该特性存在意义：按 L 未生效时确实需要尽早知道，否则会白等到黄条判定。
2. **派蒙可见时提高 `PaimonMenu` 模板阈值**。
   只治误匹配，治不了"取到旧帧"这个主因，且阈值需要按分辨率重新调参，维护成本高且不可迁移。
3. **派蒙可见时先重按 L 复检，复检仍可见则回退到黄条判定**（采用）。
   把"派蒙可见"从否定判决降级为一次可复检的信号，并保留黄条兜底。

## 决策

1. **修正语义**：派蒙可见只表示"按 L 未生效"。此时重按一次 L 后复检；
   复检仍可见则**继续走黄条判定**，不再提前退出、不再按 X。
2. **保证收敛**：重按次数上限 1 次（`PaimonEndCheckPolicy.MaxReopenAttempts`），
   用尽后必然进入黄条判定，因此"派蒙始终被判为可见"最多只会让本次检查多等一轮。
3. **修正延时**：`MinDelayMs = 150ms`，配置项 `PaimonEndCheckDelay` 默认改为 `0.2s`，
   `clamp` 范围由 0.05~0.4 收窄为 0.15~0.4，UI 的 `NumberBox` 同步。
4. **等待预算不变**：重按复检多花的等待从 `detectDelayTime` 剩余额度里扣
   （`PaimonEndCheckPolicy.RemainingDelayMs`），总等待仍不超过 `detectDelayTime`，
   避免把编队界面真的等出来。

决策逻辑抽到 `PaimonEndCheckPolicy`（`internal`，经 `InternalsVisibleTo` 供单测使用），
`AutoFightTask.CheckFightFinish` 只负责输入与截图。

## 理由

- 第一性原理上，"派蒙可见"的**唯一**可靠推论是"按 L 没生效"；把它推广成"战斗未结束"
  是一次逻辑跳跃，而这次跳跃正是缺陷本身。修正后的处理与推论严格对应。
- 黄条判定是引入派蒙检测之前就在用的结束判定，是最可靠的兜底信号。
  派蒙分支的定位应是"辅助提前发现按 L 失效"，而不是"替代黄条判定"。
- 保留上限而非去掉特性：既保住按 L 失效时的早期发现能力，又使最坏情况有界。
- 延时不取更小值，直接对应 AGENTS.md 中"键鼠操作后再识图必须加适当延时（30~60ms）"的硬约束。

## 影响与回滚

影响面：`AutoFightTask.CheckFightFinish` 为 TXT 策略、JSON 策略（`AutoFightJsonTask`）
与自动连招（`AutoComboRunTask.CheckFightFinish`）共用；`TaskFightFinishDetectConfig` 的公开签名未变，
仅内部延时取值与分支流程变化，三个调用方无需改动。

回滚点：本次提交只涉及 `PaimonEndCheckPolicy.cs`（新增）、`AutoFightTask.cs`、`AutoFightParam.cs`
与两处 UI/i18n 文案。回退该提交即可恢复原行为。

验证：`Test/BetterGenshinImpact.UnitTest/GameTaskTests/AutoFightTests/PaimonEndCheckPolicyTests.cs`
覆盖不可见直接前进、首次可见触发复检、复检后必然收敛到黄条判定、
重试次数有界、延时下限不低于输入延迟、等待预算不超预算。
