# 运行时解耦总设计

## 背景

当前运行时存在以下结构性问题：

1. 调度器、截图器、游戏会话、脚本运行共用一条启动与停止链路。
2. 截图器重启会通过 UI 停止逻辑触发全局取消，导致运行器中的任务与脚本被中断。
3. JavaScript 宿主对象在初始化阶段直接访问游戏上下文，导致脚本在截图器关闭时无法运行。
4. 全局静态入口过多，导致截图能力、调度能力、会话能力之间边界模糊。

## 当前结构

当前关键耦合点：

1. `TaskTriggerDispatcher` 同时负责：
   - 游戏会话初始化
   - 截图器创建与启动
   - 帧循环与触发器调度
   - 窗口变化时的重启控制
2. `HomePageViewModel.Stop()` 负责：
   - 停止截图器
   - 取消全局运行令牌
   - 让 `TaskContext` 失效
3. `ScriptService.StartGameTask()` 负责：
   - 保证截图器启动
   - 等待进入可执行 UI
4. `EngineExtend.InitHost()` 在构建引擎时无条件注入依赖 `TaskContext` 的宿主对象。

这意味着当前系统默认假设：

1. 运行器依赖截图器
2. 脚本依赖截图器
3. 调度器拥有截图器
4. 截图器生命周期等价于运行时生命周期

## 重构目标

### 目标一

让调度器、截图器、脚本运行器在结构上解耦，不再共享单一生命周期。

### 目标二

让截图器重启不再打断运行器中的任务与脚本。

### 目标三

让一部分 JavaScript 可以在截图器关闭时运行。

### 目标四

保留现有功能行为与 UI 操作方式，按阶段迁移，降低回归风险。

## 目标结构

长期目标结构如下：

```text
RuntimeCoordinator
|- GameSessionService
|- CaptureService
|- TriggerScheduler
|- ScriptRuntimeService
```

说明：

1. `GameSessionService` 管理与游戏窗口绑定的会话上下文。
2. `CaptureService` 只管理截图器生命周期与取帧。
3. `TriggerScheduler` 只负责消费帧并调度触发器。
4. `ScriptRuntimeService` 只负责脚本运行与宿主能力注入。
5. `RuntimeCoordinator` 作为薄协调层决定各服务的启动顺序与依赖关系。

长期上不推荐“截图器依赖调度器”，因为调度器本质是帧消费者，不应成为截图器的宿主。

## 运行时职责划分

### GameSessionService

职责：

1. 绑定游戏窗口句柄
2. 初始化 `TaskContext`
3. 暴露游戏会话是否已附着
4. 维护与窗口相关的上下文更新，例如捕获区域变化

不负责：

1. 启动截图器
2. 启动调度器
3. 取消运行中的任务

### CaptureService

职责：

1. 创建与启动 `IGameCapture`
2. 停止与重启截图器
3. 暴露当前截图状态
4. 提供统一的截图访问入口

不负责：

1. 初始化 `TaskContext`
2. 管理触发器
3. 决定脚本是否可运行

### TriggerScheduler

职责：

1. 定时拉取帧
2. 构造 `CaptureContent`
3. 调度 `ITaskTrigger`
4. 管理触发器列表和优先级

依赖：

1. `GameSessionService`
2. `CaptureService`

不负责：

1. 创建截图器
2. 停止运行器

### ScriptRuntimeService

职责：

1. 构建 JS 引擎
2. 注入宿主对象
3. 根据脚本运行级别决定依赖能力
4. 管理脚本自身的取消令牌

依赖：

1. `Standalone`
   - 无外部运行时服务依赖，只使用进程内基础宿主能力，不要求会话或截图能力
2. `GameWindow`
   - 依赖 `GameSessionService`
3. `Capture`
   - 依赖 `GameSessionService`
   - 依赖 `CaptureService`

约束：

1. `ScriptRuntimeService` 不直接依赖 `TriggerScheduler`
2. `ScriptRuntimeService` 在 `Capture` 级别也不直接缓存跨版本的 `IGameCapture` 实例，而是通过 `CaptureService` 获取当前有效的截图能力

## 脚本运行级别

为了解决“无截图运行 JS”问题，脚本需要显式声明运行级别。

建议模型：

1. `Standalone`
   - 允许：文件、HTTP、通知、日志、纯逻辑计算
   - 不允许：识图、游戏截图、触发器、依赖游戏窗口的输入换算
2. `GameWindow`
   - 允许：需要游戏窗口句柄、分辨率、DPI、输入模拟但不依赖截图的脚本
   - 不允许：识图与帧相关能力
3. `Capture`
   - 允许全部现有能力

兼容性策略：

1. 新字段缺失时默认 `Capture`
2. 旧脚本行为完全保持不变

## 取消域拆分

当前最大问题之一是截图器停止会走全局取消。

目标拆分如下：

1. `ApplicationToken`
   - 应用退出时取消
2. `SessionToken`
   - 游戏会话失效时取消
3. `CaptureToken`
   - 截图器实例重启时替换，不影响运行器
4. `RunToken`
   - 单次脚本或单次任务的执行取消令牌

迁移后约束：

1. 截图器重启只能替换 `CaptureToken`
2. 截图器重启不能直接取消 `RunToken`
3. 只有用户显式停止或会话彻底失效时，才取消 `RunToken`

### CaptureToken 轮换协调策略

第二阶段选择机制 B：`CaptureVersion` 检查 API。

策略定义：

1. 持有 `RunToken` 的长生命周期任务不会因为 `CaptureToken` 轮换而被取消，`RunToken` 保持有效直到用户显式停止或 `SessionToken` 失效。
2. 所有依赖截图的代码路径在进入截图边界前，都必须先从 `CaptureService` 读取当前 `CaptureVersion`，再调用 `EnsureCaptureReadyAsync(expectedVersion, timeout)` 或等价 API 校验捕获能力是否已准备完成。
3. 如果调用方持有的 `CaptureVersion` 与 `CaptureService` 当前版本不一致，则不得继续使用旧的 `IGameCapture` 或旧帧上下文，而是暂停当前捕获步骤、等待新版本就绪，并刷新本地 `CaptureVersion` 后再继续。
4. 第二阶段后禁止长生命周期对象跨版本缓存 `IGameCapture`；允许缓存版本号，但每次取帧都必须重新通过 `CaptureService` 获取当前有效实例。

检查点：

1. `TriggerScheduler` 的帧循环入口在每次调度前检查 `CaptureVersion`
2. `TaskControl.CaptureToRectArea`、通知截图和画中画截图等公共截图辅助入口在取帧前检查 `CaptureVersion`
3. `ScriptRuntimeService` 暴露的 JS 截图相关宿主能力在发起截图前检查 `CaptureVersion`
4. 所有未来依赖 `RunToken` 的截图型长任务，在每个“取帧边界”处执行版本检查与等待逻辑，而不是在失败后才被动恢复

## 分阶段实施

### 第一阶段：显式边界抽离

目标：

1. 把游戏会话与截图器从 `TaskTriggerDispatcher` 中抽出为独立服务
2. 保持 UI 与现有行为不变

交付：

1. `GameSessionService`
2. `CaptureService`
3. `TaskTriggerDispatcher` 改为消费这两个服务

预期收益：

1. 先把结构边界立起来
2. 为后续取消域拆分与脚本运行级别打基础

### 第二阶段：截图重启不再打断运行器

目标：

1. 把截图器重启从 UI Stop/Start 链路中移除
2. 让截图服务在内部自恢复
3. 在 `CaptureToken` 轮换期间保持 `RunToken` 有效，并让所有取帧路径通过版本检查完成平滑切换

交付：

1. `CaptureService` 内部重启与 `CaptureVersion` 轮换，移除对 `UiTaskStopTickEvent` 和 `UiTaskStartTickEvent` 的重启依赖假设
2. `CaptureService` 对单次取帧暴露固定重试策略：最多 3 次，建议间隔 100ms；每次失败后由 `TriggerScheduler` 记录日志、短暂等待 100ms，再进入下一次取帧尝试，并保留当前 `RunToken`
3. `TriggerScheduler` 在 3 次重试全部失败后执行降级：暂停当前周期的触发器调度，返回 `EmptyFrame` 占位，并发出 `CaptureUnavailable` 与 `CaptureDegraded` 事件供 UI 与上层逻辑感知
4. 当截图累计不可用超过 5s 时，`CaptureService` 触发 `PermanentFailure` 事件，并调用上层 `NotifyUser` / `UiNotification` 机制，由协调层决定是否提示用户或请求人工恢复
5. 所有截图调用路径在取帧前执行 `CaptureVersion` 校验；版本不匹配时等待新版本就绪，而不是取消 `RunToken`

预期收益：

1. 解决“截图器重启打断运行器”问题
2. 为第三阶段脚本按运行级别消费截图能力提供稳定的轮换契约

### 第三阶段：脚本运行级别与宿主能力分层

目标：

1. 按脚本运行级别构建宿主环境
2. 让 `Standalone` 和 `GameWindow` 脚本可脱离截图器运行

交付：

1. manifest 新增运行级别字段
2. `ScriptRuntimeService`
3. 宿主对象懒初始化
4. `EngineExtend` 按能力注入宿主

预期收益：

1. 解决“某些 JS 在截图器关闭时仍可运行”问题

### 第四阶段：移除遗留全局入口

目标：

1. 降低对 `TaskTriggerDispatcher.GlobalGameCapture` 和静态 `TaskContext` 读取的依赖

交付：

1. 截图统一从 `CaptureService` 获取
2. 调度统一从 `TriggerScheduler` 获取
3. 运行时能力统一从协调器或服务层获取

## 第一阶段实施范围

本次改动只实施第一阶段：

1. 新增 `GameSessionService`
2. 新增 `CaptureService`
3. 调整 `TaskTriggerDispatcher` 使用显式会话与截图服务
4. `HomePageViewModel` 停止时显式分离会话结束动作

本阶段不处理：

1. 截图器重启导致的全局取消
2. JS 运行级别
3. 宿主对象按能力注入

## 风险与回归点

1. 初始化时序兼容性风险（高）
   - 风险：`TaskContext` 初始化时机与 `GlobalGameCapture` 启动顺序改变后，依赖旧顺序的路径可能暴露隐藏耦合。
   - 缓解：第一阶段保留 `TaskTriggerDispatcher.GameCapture` 与 `GlobalGameCapture` 兼容入口，并在阶段切换前补充生命周期集成测试。
2. 对外 API 兼容风险（高）
   - 风险：`TaskTriggerDispatcher` 对外 API 若收缩过快，会影响现有页面、通知、截图和画中画逻辑。
   - 缓解：在第二阶段前保留兼容适配层，只有在回归矩阵通过后才移除旧入口。
3. 性能风险（中）
   - 风险：新增服务层级可能带来额外调用开销，影响帧率、截图延迟或启动时延，尤其与 `TaskContext` 初始化时机和 `GlobalGameCapture` 启动顺序相关。
   - 缓解：建立重构前后基准场景，对比平均帧间隔、P95 取帧延迟、冷启动时延和重启恢复时延；第二、三阶段只有在性能回归可接受时才允许推进。
4. 测试覆盖风险（高）
   - 风险：若缺少针对 `TaskTriggerDispatcher` 对外 API、截图公共入口和脚本宿主能力的单元/集成/端到端测试，容易在阶段切换时遗漏回归。
   - 缓解：把测试矩阵作为阶段验收前置条件，并明确每个阶段必须新增的测试类型与覆盖场景。
5. 多线程安全风险（高）
   - 风险：`CaptureService`、`TriggerScheduler` 与 `ScriptRuntimeService` 在跨线程访问共享状态、轮换 `CaptureToken` 或更新版本号时可能出现竞态。
   - 缓解：对共享状态使用锁、原子版本快照或不可变状态对象，并增加“运行中重启截图器”“脚本执行中切换截图版本”等并发测试。
6. 回滚风险（中）
   - 风险：若第二阶段后出现阻塞性回归，恢复旧路径的成本会随阶段推进而上升。
   - 缓解：保留按阶段回滚能力，回滚条件为“关键场景无法恢复”或“回归矩阵出现阻塞失败”；回滚步骤优先恢复 `App.xaml.cs` 服务接线、`TaskTriggerDispatcher` 的截图宿主关系与 `HomePageViewModel` 停止链路，预计成本 0.5 到 1 个工作日。

## 验证策略

### 第一阶段测试计划

1. 编译与静态检查（自动化测试）
   - 执行 `dotnet build` 验证工程可编译
   - 执行分析器与问题面板检查，确认未引入新的阻塞性错误
2. 生命周期独立性测试（需要新增单元测试 / 集成测试）
   - 单元测试：`GameSessionService.Attach/Detach` 只影响会话上下文，不启动或停止截图器
   - 单元测试：`CaptureService.Start/Stop` 只影响截图状态，不修改触发器列表或运行取消令牌
   - 集成测试：`TaskTriggerDispatcher.Start` 先调用 `GameSessionService.Attach`，再启动 `CaptureService`
3. 服务边界与职责越界测试（需要新增单元测试）
   - 验证 `CaptureService` 不直接管理触发器、不直接取消 `RunToken`
   - 验证 `GameSessionService` 不负责创建或停止截图器
   - 验证未来 `ScriptRuntimeService` 在 `Standalone` / `GameWindow` 级别不依赖 `TriggerScheduler`
4. 编译 + 运行时行为验证（自动化测试 + 手动测试）
   - 自动化：对 `TaskTriggerDispatcher.GameCapture`、`GlobalGameCapture` 与公共截图辅助入口进行编译级兼容性检查
   - 手动：验证截图器启动、停止、重新进入页面后的行为与现状一致
   - 手动：验证任务调度、截图、通知截图、画中画的运行时行为没有回归
5. 生命周期联动验证（需要新增集成测试）
   - 验证 `GameSessionService` 与 `CaptureService` 在服务层可被独立驱动和断言
   - 验证当前 UI / 调度链路仍保持第一阶段既有行为：停止完整运行时会话时不会出现隐式副作用扩散
6. 性能对比验证（需要新增集成测试 / 基准测试）
   - 基准场景 A：1920x1080，BitBlt，空闲触发器循环 60s
   - 基准场景 B：1920x1080，常用触发器集启用，持续取帧 60s
   - 基准场景 C：启动截图器、停止截图器、再次启动
   - 指标：平均帧间隔、P95 取帧延迟、启动耗时、重启恢复耗时
7. 回归测试矩阵（手动测试 + 需要新增集成测试）

| 场景 | 类型 | 通过标准 |
| --- | --- | --- |
| 启动截图器并进入稳定循环 | 手动测试 | 遮罩、触发器、截图能力正常工作 |
| 停止截图器 | 手动测试 | 运行时会话按预期结束，无额外异常 |
| `TaskTriggerDispatcher.GameCapture` / `GlobalGameCapture` 兼容入口 | 自动化测试 | 现有调用点编译通过，运行时可取到有效实例 |
| 通知截图与画中画 | 手动测试 + 集成测试 | 截图与显示功能正常，无空引用异常 |
| 服务层单独启动/停止会话与截图器 | 需要新增集成测试 | 两个服务可分别驱动并断言边界副作用 |
| 会话附着后刷新捕获区域 | 需要新增集成测试 | 捕获区域更新只影响会话上下文，不越界修改其它状态 |

### 阶段验收标准

1. 第一阶段
   - 新增的 `GameSessionService` 与 `CaptureService` 完成接线
   - 兼容入口仍可用
   - 无本阶段新增的阻塞性编译错误
2. 第二阶段
   - 截图器重启不再通过 UI Stop/Start 打断运行器
   - `CaptureVersion` 轮换与降级策略可观测、可测试
3. 第三阶段
   - `Standalone` 与 `GameWindow` 级别脚本可在无截图器场景下运行
   - `Capture` 级别脚本通过 `CaptureService` 获取截图能力

## 小结

本设计的核心不是简单把截图器从调度器里拆出去，而是把“会话、截图、调度、脚本运行”恢复为独立职责，并通过清晰的取消域和版本轮换契约消除重启打断问题。

截至当前分支，第一阶段骨架已经开始落地：`GameSessionService` 与 `CaptureService` 已经引入，`TaskTriggerDispatcher` 已改为显式消费这两个服务。但第二阶段的重启自恢复和第三阶段的脚本运行级别仍需按本文推进。

## 下一步建议

1. 先为第一阶段补齐生命周期独立性测试、服务边界测试和回归矩阵中的必过场景。
2. 进入第二阶段，实现 `CaptureVersion`、降级事件和长期不可用时的最终失败路径。
3. 在第二阶段稳定后再推进第三阶段，引入脚本运行级别与按能力注入的宿主模型。
