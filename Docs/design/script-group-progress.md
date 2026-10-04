# 配置组执行进度与预计剩余时间

状态：已实施（2026-10）。

## 1. 背景与目标

配置组（调度器 / 一条龙 / 跨用户 Worker）执行时，用户只能看到「正在执行某个脚本」的日志，无法判断整组还剩多久。
本功能在执行期间展示进度面板，执行结束后自动隐藏；开始执行时的日志与通知也会带上预计剩余时间。

```text
正在执行：每日
[========------------]
预计剩余时间：1:27
预计总剩余时间：12:34        ← 只有一次执行涉及多个配置组时才出现
```

目标：

- 不引入额外配置项，开关即「配置组里有没有地图追踪脚本」。
- 估算误差控制在「量级正确」即可，不追求精确。
- 每跑完一个脚本就按剩下的脚本重算一次预计剩余时间，不让前面脚本的误差一直背着。
- 主窗口与游戏内叠加层都要显示，且不遮挡已有内容。
- 跨用户 Worker 场景下，控制端主窗口显示的进度与 Worker 叠加层是同一份数据。

非目标：JS / 键鼠 / Shell 脚本的耗时估算。

### 1.1 两个时间

| 显示 | 含义 | 何时出现 |
| --- | --- | --- |
| 预计剩余时间 | **当前配置组**剩余：本组还没开始的脚本预估 + 当前脚本的剩余预估 | 有预估时始终显示 |
| 预计总剩余时间 | 本次执行**整批**剩余：当前配置组剩余 + 后续配置组全部预估 | 只在当前配置组**之后还有配置组**时显示 |

两个数字是分开算的，不做合并：只跑一个配置组时看不到第二行；连续执行 / 一条龙跑多个配置组时多出这一行，
跑到最后一个配置组时它又会消失（此时两个数字本来就相同，留着反而像出错）。

倒计时向上取整、已用时间向下取整：60 秒的脚本一开始显示 `1:00` 而不是 `0:59`，也不会提前跳到 `0:00`。
小时数取**总小时数**而不是 `TimeSpan` 的小时分量：总剩余 28 小时 35 分显示为 `28:35:24`，
不会在 24 小时处回绕成 `4:35:24`（那会比其中单个 15 小时的任务还小）。

## 2. 估算模型

地图追踪 JSON 的执行时间被拆成三部分：

| 部分 | 取法 | 参数 |
| --- | --- | --- |
| 移动 | 相邻点位 2D 距离 / 速度，速度取**目标点位**的 `move_mode` | `walk` 4.0、`run` 5.0、`dash` 5.5、`fly` 4.0、`jump` 2.5、`climb` 2.0、`swim` 3.0，未知 4.0（单位/秒） |
| 点位开销 | 按 `type` 固定累加 | `teleport` 8.0、`target` 2.5、`orientation` 2.0、`path` 1.0，未知 1.0（秒） |
| 动作开销 | `action` 取第一个空格前的动作名 + `action_params` 中所有 `wait(n)` 累加 | `combat_script` 10.0、`stop_flying` 1.5，其他动作 0（秒） |

配置组总时长 = 组内所有**启用**的地图追踪脚本的估算值 × `RunNum`，非地图追踪脚本按 0 计。
总时长为 0（组里没有地图追踪）时不显示进度。

### 2.1 进度与两个剩余时间

一次执行被建模成一张**脚本计划**（`ScriptGroupProgressPlan`）：按顺序排列的脚本，每个脚本带所属配置组与预估秒数。

| 量 | 算法 |
| --- | --- |
| 进度条 | `(本组已结束脚本的预估 + min(当前脚本已用时, 当前脚本预估)) / 本组预估总和`，只反映**当前配置组**，进入下一个配置组时重新按 0 开始；只增不减（补时会让分母变大，用下限保证不回退） |
| 预计剩余时间 | `本组未开始脚本的预估总和 + max(0, 当前脚本预估 - 当前脚本已用时)` |
| 预计总剩余时间 | `预计剩余时间 + 后续配置组全部脚本的预估总和`，后续配置组从**本组结束位置**起算（不是「当前脚本 + 1」，否则会漏掉下一组的第一个脚本） |

**超时保护**：当前脚本跑满或跑超自己的预估时，按 **10 秒**粒度继续补时，还超就继续补，
保证倒计时始终是正数 —— 否则它的剩余会被夹在 0，界面上看起来就是「倒计时卡住了」
（用 `>=` 判断，正好跑满也会再补一轮，而不是显示 `0:00` 停住）。
补时只加在当前脚本上，脚本一结束剩余时间就按后面的脚本重新算，不会把补出来的时间带下去。

- 每跑完（或被跳过）一个脚本，它就从「剩余」里消失：下一个脚本开始时会重算，所以预计剩余时间会在脚本边界跳一次。
- 被跳过、被禁用的脚本不会单独上报结束：它们的下标直接越过，同样立刻退出剩余时间。
- 已经跑完的脚本用的是**预估**权重（不回头修正已用时间），所以剩余时间始终是「剩下的脚本预计还要多久」。

**回血补时**：执行途中触发回血会给当前脚本补时，避免预估被回血开销吃掉。
回七天神像的场景还要加上玩家在**设置 → 通用设置 → 回血等待间隔（秒）**（`TpConfig.HpRestoreDuration`）里配的等待时长 ——
`TpTask.TpToStatueOfTheSeven` 传送到神像后会按该配置等待，所以它本身就是这段耗时的一部分。

| 场景 | 处理 |
| --- | --- |
| 队伍回血（白术 / 希格雯 / 心海） | 当前脚本 `+30` 秒，路线继续（`NotifyHealTriggered()`，不含神像等待） |
| 回七天神像 / 复苏后重走路线（`RecoverWhenLowHp`，随后抛 `RetryException` 重试） | **重置当前脚本计时**再 `+30 + 回血等待间隔`（`NotifyRouteReset(GetStatueHealExtraSeconds())`），因为脚本从头再来 |
| 战斗中被击败后 `Avatar.TpForRecover` | `+30 + 回血等待间隔` |
| 切换队伍前发现全队死亡（`SwitchPartyBefore`） | 还没进路线，不重置计时，`+30 + 回血等待间隔` |

`ScriptGroupProgressTracker.GetStatueHealExtraSeconds(hpRestoreWaitSeconds)` 只做「基准 + 等待」的加法，
**等待时长由调用方从配置读出后传入**：本类在 `Core` 里且被单测直接构造，
不能触碰 `TaskContext.Instance().Config` —— 那会连带 `ConfigService` 静态初始化与主程序启动副作用
（提权弹 UAC、占用单实例命名管道），单测宿主会直接崩溃。

## 3. 代码位置

| 位置 | 职责 |
| --- | --- |
| `GameTask/AutoPathing/PathingTimeEstimator.cs` | 纯函数估算，输入 `PathingTask` 或 `IReadOnlyList<Waypoint>`，输出秒 |
| `Core/Script/Group/ScriptGroupProgressPlan.cs` | 计划与进度计算，**纯逻辑、不依赖 WPF**，由 `ScriptGroupProgressPlanTests` 守护 |
| `Core/Script/Group/ScriptGroupProgressTracker.cs` | 单例 + `ObservableObject`，持有计划、`Stopwatch` 与 1 秒一次的 `System.Threading.Timer`（回调经 `Dispatcher.BeginInvoke` 排进 UI 队列），暴露 `IsRunning` / `GroupName` / `Progress` / `ElapsedText` / `RemainingText` / `HasTotalRemaining` / `TotalRemainingText`；`StateChanged` 事件与 `ApplyRemote` 供跨用户场景使用，`CreateIsolated` 供单测建独立实例 |
| `Service/ScriptService.RunMulti` | 唯一的启动点：`BuildSteps` 生成计划 → 启动日志与通知取本组剩余 → `using var … BeginTracking(groupName, steps)`；每个脚本开始时调用 `OnStepStarted(index)` |
| `ScriptControlViewModel.StartGroups` | 连续执行：`using var … BeginBatch(sg)` 预置整批配置组（Worker 的 `scriptGroups` / 继续执行也走这里） |
| `OneDragonFlowViewModel.OnOneKeyExecute` | 一条龙：`BeginBatch(启用的配置组任务)` 预置整批 |
| `Service/Worker/WorkerIpcService.cs` + `WorkerNoticeHub` | 无头 Worker 侧订阅 `StateChanged`，经 `worker.progress` 每秒推给已授权的控制端 |
| `Service/Worker/WorkerController.cs` | 控制端侧接收 `worker.progress` 并 `ApplyRemote`，连接断开时清空 |

跟踪的生命周期绑定在 `RunMulti` 的方法作用域上：

- `BeginTracking` 的令牌随每次执行递增，嵌套执行（优先执行配置组）时内层结束不会误关外层进度。
- 用 `using var` 而不是 `try/finally`，RunMulti 抛异常时也能保证进度被隐藏。
- 计时从 `RunMulti` 拿到执行列表后开始，不含之前等待游戏启动的时间。
- 启动日志与 `GroupStart` 通知带的是**本组**预计剩余时间，格式化成 `1分27秒`（`FormatEstimatedDuration`）；
  本组预估为 0 时两者都不带预计时间。

### 3.1 批量执行

`BeginBatch` / `BeginBatchSteps` 做三件事：把整批配置组的脚本预置进计划（用于算总剩余）、标记「这是一批」、
**重新起表**（`Stopwatch.Restart()`）。

- 批次是新的执行，必须重新起表：后续配置组会因为「沿用整批计时」而跳过 `Restart`，
  如果批次入口也不起表，秒表就永远停在 0，倒计时和进度条都再也不动（这是本功能踩过的坑）。
- 每个 `RunMulti` 进来时，如果计划里还有同名配置组就接着用整批的计时与计划，否则当成一次独立执行重新开始。
- 批次作用域释放、而计划里还有没跑的配置组（异常 / 手动中断）时会直接隐藏，不会留下一个永远不动的面板。
- 组与组之间（连续执行有 2 秒间隔）面板保持显示不闪，只是那两秒不刷新。

**停止时立即隐藏**：用户点停止或任务被取消时，跟踪作用域结束时**立刻隐藏并清空计划**，
不再沿用上一条「等下一个配置组接上」的显示（判据是 `CancellationContext.IsAborted`）。
否则批量循环会把剩下的配置组逐个空转一遍，每进一组面板就往前推一格，
界面看起来就是「倒计时被快速走完」。

同时 `StartGroups` 的循环里加了取消检查，停止后不再启动剩余的配置组：
`RunMulti` 开头会 `CancellationContext.Set()` 重设取消状态，所以必须在循环里自己判断；
且只在「本批已经跑过至少一个配置组」之后才看取消状态，避免上一次手动停止残留的 `IsManualStop` 把新批次误杀。

### 3.2 刷新用的定时器与节拍

读数刷新走 `System.Threading.Timer`，回调只把一次刷新用 `Dispatcher.BeginInvoke` 排进 UI 队列。

**不能用 `DispatcherTimer`**：任务期间日志与绘制会把 UI 线程占满，低优先级（`Background`）的
`DispatcherTimer` 会被饿死，表现为「倒计时跑一会就不动了」。定时器跑在线程池上就与 UI 负载无关。

**刷新时刻对齐到整秒边界**（`GetDelayUntilDisplayChanges`）：倒计时是向上取整后的秒，
所以「剩余时间的小数部分走完」就是它变化的时刻，每次都按这个延迟安排**一次性**定时器，而不是固定 1 秒周期。
固定周期与整秒边界各跑各的会错位 —— 两次刷新落在同一个显示区间就是「等 2 秒没动」，
两次刷新跨过两个边界就是「1 秒掉 2 秒」。对齐之后每次刷新恰好掉 1 秒（`AlignedRefresh_ShouldDropExactlyOneSecondPerTick` 守护）。

剩余时间本身是 `Stopwatch` 的 double 秒，但浮点误差在 1e-13 量级，与「秒」的显示无关；
读数跳变只可能来自这三种**本来就该跳**的情况：脚本/配置组边界重算、超时保护 +10 秒、回血补时 +30 秒。

### 3.3 可测性

- 进度与剩余时间的计算全在 `ScriptGroupProgressPlan` 里，纯逻辑，`ScriptGroupProgressPlanTests` 直接覆盖。
- `ScriptGroupProgressTracker` 的时序用 `CreateIsolated()`（`internal`，供单测）建独立实例测：
  共享单例会被其它代码路径的清空影响，而进度跟踪依赖真实时钟，断言必须跑在干净实例上。
- 单测宿主没有 `Application.Current`，`StartTicker` 会直接返回，因此测试里刷新只由各接口调用触发，
  断言是确定的（`ScriptGroupProgressTrackerTests` 用「秒表确实在走」这条来守护倒计时本身）。

## 4. 展示位置

两个时间各占一行，第二行由 `HasTotalRemaining` 控制显隐（折叠时不占位）。
叠加层的让位由 `MaskWindow.xaml.cs` 的 `UpdateRightSideOverlayOffset` 完成：遍历 `OverlayCanvas` 的子元素，
对 `Canvas.Left >= 画布宽度 / 2` 且 `Canvas.Top < 面板高度` 的元素设置 `TranslateTransform`。
只处理会被遮挡的元素，避免把底部元素推出屏幕；元素位置本身（`Canvas.Left/Top` 绑定）不变。

叠加层字号跟随状态栏的那套缩放（`OverlayScaledSizeConverter` + `StatusFontSize` / `LogFontScale` / `ScaleTo1080PRatio`），
多分辨率和非 100% DPI 下与其他叠加层元素保持一致。

### 4.1 跨用户 Worker

任务在 Worker 进程内执行，进度也在 Worker 侧计算，然后每秒通过 `worker.progress` 回传：

```text
Worker: ScriptGroupProgressTracker.StateChanged → WorkerNoticeHub.PublishProgress → worker.progress
Controller: WorkerClientConnectionOwner → WorkerController.OnProgressReceived → ApplyRemote
```

- 控制端只是把收到的状态刷到同一个单例上，**不启动本地计时器**，因此两边显示完全一致；
- `worker.progress` 会连「预计总剩余时间」一起带过去，所以控制端也能看到第二行；
- `StateChanged` 只在状态真的变化时触发，不会因为重复赋值刷屏；
- Worker 连接断开（含 Worker 退出）时控制端主动清空进度，面板不会停在最后一帧。

## 5. 已知限制

- 只统计地图追踪 JSON，组内 JS / 键鼠 / Shell 脚本不产生时间，因此混合配置组的剩余时间会偏小；
  一条龙里的非配置组任务同样不参与预估。
- 战斗策略（`combat_script`）固定按 10 秒计，实际耗时取决于战斗策略脚本。
- 预计剩余时间减到 0 后不隐藏、也不停止进度条，只把剩余时间显示为 `0:00`。
- 循环执行（`Loop: true`）只按一轮预估，不乘循环次数。
- 跨用户场景下控制端依赖 Worker 每秒推送，Worker 卡死或管道中断时进度会停在最后一帧（断连时清空）。
