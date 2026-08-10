# 地图目标导航 P0–P8 设计

日期：2026-07-16
范围：完成地图点击目标到 `PathExecutor` 执行的可靠闭环；P9 实机验收由用户完成。

## 背景与根因

当前地图点击会更新目标坐标，手动规划也能调用 `RouteNavigationPlanner`，但规划结果只通过 Messenger 发给地图，没有保存为可执行状态。顶部“开始追踪”在调试模式错误调用 `RunRecording`，因此读取的是空录制路线。调试模式还没有在启动时主动识别实时坐标，导致界面长期显示“等待坐标”。

## 架构选择

采用可扩展的目标导航工作流，而不是继续把流程堆进 `MapViewerViewModel`：

- `TargetNavigationWorkflow`：纯编排层，负责预检、计划复用/重规划、保存计划、执行和状态转换。
- `ITargetNavigationRuntime`：BetterGI 运行时适配层，负责启动截图器、窗口检查与激活、主界面识别、实时定位、`TaskRunner` 和 `PathExecutor`。
- `IRouteNavigationPlanner`：为现有规划器增加可测试接口。
- `IRouteCoordinateConverter`：统一地图图像/特征坐标和原神游戏坐标转换；规划器只接收 `RouteGraphPoint`，生成给 `PathExecutor` 的 `Waypoint` 时统一转换。
- `MapViewerViewModel`：只保存用户目标、计划、可执行任务、当前执行任务和界面状态，不再承担底层运行细节。

该工作流后续可以作为 PRD 中 `HierarchicalNavigator` 的第一阶段：路网执行完成后，继续挂接任务标记、黄色地面指引和局部恢复，而不需要重写本次入口。

## 显式路线状态

ViewModel 分别维护：

- `_currentDisplayedTask`：当前地图展示路线；
- `_currentRecordedTask`：当前录制/编辑路线；
- `_currentPlan`：当前路网规划结果；
- `_currentExecutableTask`：下一次允许执行的规划任务；
- `_executingTask`：当前由 `PathExecutor` 执行的任务。

Messenger 仅负责投影到界面，不能作为业务状态存储。

## 完整数据流

```text
地图点击目标（图像/特征坐标 + 地图名）
→ RunTargetNavigationCommand
→ 检查目标与 TaskRunner 占用
→ ScriptService.StartGameTask(false)
→ 检查截图器和游戏窗口
→ 激活并验证原神前台窗口
→ 检查主界面
→ 识别当前实时图像坐标和实际地图
→ 校验当前地图与目标地图
→ 有效旧计划则复用，否则调用 RouteNavigationPlanner
→ 保存 _currentPlan / _currentExecutableTask
→ 更新当前任务卡片并发送地图展示消息
→ TaskRunner
→ PathExecutor.Pathing(_executingTask)
→ 根据 SuccessEnd / 取消 / 异常更新状态
→ finally 释放全部键盘和鼠标按键
```

手动“规划路线”按钮复用同一规划入口，但强制重新规划且不执行。

## 计划有效性

已有计划仅在以下条件全部满足时复用：

- 计划成功且包含至少两个可执行点的 `PathingTask`；
- 地图名、目标图像坐标、目标动作、移动方式和规划选项未改变；
- 当前实时坐标仍在允许的起点接入距离内。

目标、地图或相关规划参数改变时立即使计划失效。

## 失败原因

工作流使用稳定失败代码并映射为明确中文：

- 路网文件不存在；
- 路网为空或损坏；
- 当前坐标不可识别；
- 当前点无法接入路网；
- 目标点无法接入路网；
- 当前地图和目标地图不一致；
- 没有可用路径；
- 传送点不可用；
- 截图器未初始化；
- 原神窗口不存在；
- 当前不在主界面；
- 目标尚未选择；
- 路网尚未加载；
- 其他独立任务占用 TaskRunner；
- 原神窗口激活失败或执行中失去前台；
- 坐标转换失败；
- PathExecutor 未完成、异常或用户取消。

失败会同时更新顶部当前任务卡片和规划摘要；规划/执行失败后重新激活地图窗口以便用户直接看到原因。

## 导航状态

状态机至少包含：

`未选择目标 → 等待启动 → 正在规划 → 规划失败 / 规划成功 → 等待启动 → 正在执行 → 执行完成 / 执行失败 / 用户取消`

顶部按钮在调试模式直接使用 `RunTargetNavigationCommand`；执行期间显示“取消追踪”。录制模式继续使用录制开关命令。

## 输入安全

工作流最外层、`TaskRunner` 执行动作、窗口关闭和失焦监控都调用统一释放逻辑。释放范围包括所有检测到的键盘键，以及鼠标左、中、右和 X1/X2 按键。执行中失去原神前台时立即取消并释放输入，禁止继续向其他窗口发送移动按键。

## 测试

先写失败测试并确认 RED，再实现：

- 成功流程的调用顺序和同一 `plannedTask` 传递；
- 有效计划复用、目标/起点变化自动重规划；
- 每种规划和预检失败均不启动执行；
- 状态转换完整；
- 执行异常、取消、锁占用和失焦均释放输入；
- 规划器生成的 `Waypoint` 使用统一坐标转换；
- 路网缺失、空、损坏及无连接路径返回稳定失败代码。

最终运行针对性单元测试、相关回归测试和 Debug 构建。P9 需要真实原神与 BetterGI 环境，由用户执行。
